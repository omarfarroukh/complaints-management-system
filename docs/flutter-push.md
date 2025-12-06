Here is a complete, drop-in Markdown file (flutter-pushnotifs.md) tailored exactly to your backend implementation. It covers setup, API integration, and handling the "Data-Only" message structure you implemented.

Flutter Push Notification Implementation Guide
1. Overview

We use Firebase Cloud Messaging (FCM). The backend sends "Data-Only" messages (no notification block) to prevent system tray duplicates and allow full UI customization.

Backend Tech Stack

Provider: Firebase Cloud Messaging (FCM)

Payload Type: Data-only (Silent push)

Keys Used: title, body, url, relatedEntityId

2. Dependencies

Add these packages to your pubspec.yaml:

code
Yaml
download
content_copy
expand_less
dependencies:
  firebase_core: latest
  firebase_messaging: latest
  flutter_local_notifications: latest # Required for displaying the notification manually
  http: latest # Or dio, for API calls
3. Configuration Setup

(Ask the team lead for the google-services.json and GoogleService-Info.plist files if you don't have them).

Android: Place google-services.json in android/app/.

iOS: Place GoogleService-Info.plist in ios/Runner/.

iOS Capability: Ensure "Push Notifications" and "Background Modes -> Remote notifications" are enabled in Xcode.

4. API Endpoints

You must sync the FCM token with our backend.

A. Register Device (Call on Login / App Start)

Endpoint: POST /api/Device/register

Auth: Bearer Token required.

Body:

code
JSON
download
content_copy
expand_less
{
  "token": "FCM_TOKEN_STRING",
  "platform": "Android" // or "iOS"
}
B. Unregister Device (Call on Logout)

Endpoint: POST /api/Device/unregister

Auth: Bearer Token required.

Body:

code
JSON
download
content_copy
expand_less
{
  "token": "FCM_TOKEN_STRING"
}
C. Check Status (Optional - For Settings Screen)

Endpoint: GET /api/Device/status?token=FCM_TOKEN_STRING

Response: { "isRegistered": true }

5. Implementation Logic (The "Data-Only" Handler)

Since the backend sends data messages, the OS will NOT show a notification automatically. You must listen to the incoming message and use flutter_local_notifications to show it.

lib/services/push_notification_service.dart
code
Dart
download
content_copy
expand_less
import 'dart:io';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:http/http.dart' as http;
import 'dart:convert';

class PushNotificationService {
  static final FirebaseMessaging _firebaseMessaging = FirebaseMessaging.instance;
  static final FlutterLocalNotificationsPlugin _localNotifications = FlutterLocalNotificationsPlugin();

  // 1. Initialize
  static Future<void> initialize() async {
    // Request Permission (iOS requires this explicitly)
    NotificationSettings settings = await _firebaseMessaging.requestPermission(
      alert: true,
      badge: true,
      sound: true,
    );

    if (settings.authorizationStatus == AuthorizationStatus.authorized) {
      print('User granted permission');
      
      // Init Local Notifications (Android Icon setup)
      const AndroidInitializationSettings initializationSettingsAndroid =
          AndroidInitializationSettings('@mipmap/ic_launcher'); // Ensure icon exists
      
      const DarwinInitializationSettings initializationSettingsIOS = DarwinInitializationSettings();
      
      const InitializationSettings initializationSettings = InitializationSettings(
        android: initializationSettingsAndroid,
        iOS: initializationSettingsIOS,
      );

      await _localNotifications.initialize(
        initializationSettings,
        onDidReceiveNotificationResponse: (details) {
          // Handle tap logic here based on payload
          if (details.payload != null) {
            _handleNotificationTap(details.payload!);
          }
        },
      );

      // Listen to Foreground Messages
      FirebaseMessaging.onMessage.listen(_handleForegroundMessage);
      
      // Listen to Background/Terminated Messages (Data-only handling)
      FirebaseMessaging.onBackgroundMessage(_firebaseMessagingBackgroundHandler);
      
      // Get & Sync Token
      String? token = await _firebaseMessaging.getToken();
      if (token != null) {
        print("FCM Token: $token");
        // TODO: Call your API Register Endpoint here
      }
    }
  }

  // 2. Handle Incoming Message
  static void _handleForegroundMessage(RemoteMessage message) {
    print("Received Message: ${message.data}");
    
    // The backend sends 'title' and 'body' inside the data payload
    String? title = message.data['title'];
    String? body = message.data['body'];
    
    if (title != null && body != null) {
      _showLocalNotification(
        id: message.messageId.hashCode,
        title: title,
        body: body,
        payload: jsonEncode(message.data), // Pass data for click handling
      );
    }
  }

  // 3. Show Local Notification
  static Future<void> _showLocalNotification({
    required int id,
    required String title,
    required String body,
    required String payload,
  }) async {
    const AndroidNotificationDetails androidPlatformChannelSpecifics =
        AndroidNotificationDetails(
      'cms_channel_id', // id
      'CMS Notifications', // title
      importance: Importance.max,
      priority: Priority.high,
    );
    
    const NotificationDetails platformChannelSpecifics =
        NotificationDetails(android: androidPlatformChannelSpecifics);

    await _localNotifications.show(
      id,
      title,
      body,
      platformChannelSpecifics,
      payload: payload,
    );
  }

  // 4. Handle Click Action
  static void _handleNotificationTap(String payload) {
    try {
      final data = jsonDecode(payload);
      
      // Check for Navigation Data from Backend
      if (data['url'] != null) {
         // Navigate to URL or Specific Screen
         // navigatorKey.currentState?.pushNamed(data['url']);
      } 
      else if (data['relatedEntityId'] != null) {
         // Navigate to Complaint Details
         // navigatorKey.currentState?.pushNamed('/complaint-details', arguments: data['relatedEntityId']);
      }
    } catch (e) {
      print("Error parsing payload: $e");
    }
  }
}

// 5. Background Handler (Must be Top-Level Function)
@pragma('vm:entry-point')
Future<void> _firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  // Use the same logic as foreground to show the notification manually
  // Note: LocalNotifications plugin needs to be re-initialized here in some cases
  print("Background Message: ${message.data}");
}
6. Main Entry Point

In your main.dart:

code
Dart
download
content_copy
expand_less
void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await PushNotificationService.initialize();
  
  runApp(MyApp());
}
7. Troubleshooting

No Notification? Check if the app logs Received Message. If yes, the issue is with flutter_local_notifications setup (usually missing Icon in AndroidManifest).

Android Background: If the app is killed, ensure your device isn't restricting background data for the app (Battery Optimization settings).

Backend Payload: Ensure the backend is sending title (lowercase) inside data. The code expects message.data['title'].