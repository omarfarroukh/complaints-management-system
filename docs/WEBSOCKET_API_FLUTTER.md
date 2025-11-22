# WebSocket API Documentation - Flutter/Dart

## Overview

This guide shows how to integrate real-time notifications in your Flutter app using SignalR.

---

## Installation

Add the SignalR package to `pubspec.yaml`:

```yaml
dependencies:
  signalr_netcore: ^1.3.7
  flutter_secure_storage: ^9.0.0 # For token storage
```

Run:

```bash
flutter pub get
```

---

## Connection Setup

### Basic Connection

```dart
import 'package:signalr_netcore/signalr_client.dart';

class NotificationService {
  HubConnection? _hubConnection;
  final String apiBaseUrl;
  final String accessToken;

  NotificationService({
    required this.apiBaseUrl,
    required this.accessToken,
  });

  Future<void> connect() async {
    _hubConnection = HubConnectionBuilder()
        .withUrl(
          '$apiBaseUrl/notifications?access_token=$accessToken',
          HttpConnectionOptions(
            accessTokenFactory: () async => accessToken,
            logMessageContent: true,
            skipNegotiation: true,
            transport: HttpTransportType.WebSockets,
          ),
        )
        .withAutomaticReconnect()
        .build();

    _setupEventHandlers();

    await _hubConnection!.start();
    print('✅ Connected to notifications hub');

    // Load initial notifications
    await getUnreadNotifications();
    await getUnreadCount();
  }

  void _setupEventHandlers() {
    // Listen for new notifications
    _hubConnection!.on('ReceiveNotification', _handleNewNotification);

    // Listen for notifications loaded
    _hubConnection!.on('NotificationsLoaded', _handleNotificationsLoaded);

    // Listen for unread count updates
    _hubConnection!.on('UnreadCountUpdated', _handleUnreadCountUpdated);

    // Listen for marked as read confirmations
    _hubConnection!.on('NotificationMarkedAsRead', _handleMarkedAsRead);

    // Listen for all marked as read
    _hubConnection!.on('AllNotificationsMarkedAsRead', _handleAllMarkedAsRead);

    // Listen for deleted confirmations
    _hubConnection!.on('NotificationDeleted', _handleDeleted);

    // Listen for errors
    _hubConnection!.on('Error', _handleError);
  }

  Future<void> disconnect() async {
    await _hubConnection?.stop();
  }
}
```

---

## Data Models

```dart
class NotificationDto {
  final String id;
  final String title;
  final String message;
  final String type; // Info, Success, Warning, Error
  final bool isRead;
  final DateTime createdOn;
  final DateTime? readAt;
  final String? relatedEntityType;
  final String? relatedEntityId;

  NotificationDto({
    required this.id,
    required this.title,
    required this.message,
    required this.type,
    required this.isRead,
    required this.createdOn,
    this.readAt,
    this.relatedEntityType,
    this.relatedEntityId,
  });

  factory NotificationDto.fromJson(Map<String, dynamic> json) {
    return NotificationDto(
      id: json['id'],
      title: json['title'],
      message: json['message'],
      type: json['type'],
      isRead: json['isRead'],
      createdOn: DateTime.parse(json['createdOn']),
      readAt: json['readAt'] != null ? DateTime.parse(json['readAt']) : null,
      relatedEntityType: json['relatedEntityType'],
      relatedEntityId: json['relatedEntityId'],
    );
  }
}

enum NotificationType {
  info,
  success,
  warning,
  error;

  static NotificationType fromString(String type) {
    return NotificationType.values.firstWhere(
      (e) => e.name.toLowerCase() == type.toLowerCase(),
      orElse: () => NotificationType.info,
    );
  }
}
```

---

## Client → Server Methods

### Get Unread Notifications

```dart
Future<void> getUnreadNotifications() async {
  await _hubConnection?.invoke('GetUnreadNotifications');
  // Response comes via "NotificationsLoaded" event
}
```

### Get Recent Notifications

```dart
Future<void> getRecentNotifications({int count = 50}) async {
  await _hubConnection?.invoke('GetRecentNotifications', args: [count]);
}
```

### Mark as Read

```dart
Future<void> markAsRead(String notificationId) async {
  await _hubConnection?.invoke('MarkAsRead', args: [notificationId]);
}
```

### Mark All as Read

```dart
Future<void> markAllAsRead() async {
  await _hubConnection?.invoke('MarkAllAsRead');
}
```

### Delete Notification

```dart
Future<void> deleteNotification(String notificationId) async {
  await _hubConnection?.invoke('DeleteNotification', args: [notificationId]);
}
```

### Get Unread Count

```dart
Future<void> getUnreadCount() async {
  await _hubConnection?.invoke('GetUnreadCount');
}
```

---

## Event Handlers

```dart
void _handleNewNotification(List<Object>? arguments) {
  if (arguments == null || arguments.isEmpty) return;

  final json = arguments[0] as Map<String, dynamic>;
  final notification = NotificationDto.fromJson(json);

  // Add to local state
  _notifications.insert(0, notification);
  notifyListeners();

  // Show local notification
  _showLocalNotification(notification);
}

void _handleNotificationsLoaded(List<Object>? arguments) {
  if (arguments == null || arguments.isEmpty) return;

  final jsonList = arguments[0] as List<dynamic>;
  _notifications = jsonList
      .map((json) => NotificationDto.fromJson(json as Map<String, dynamic>))
      .toList();

  notifyListeners();
}

void _handleUnreadCountUpdated(List<Object>? arguments) {
  if (arguments == null || arguments.isEmpty) return;

  final count = arguments[0] as int;
  _unreadCount = count;
  notifyListeners();
}

void _handleMarkedAsRead(List<Object>? arguments) {
  if (arguments == null || arguments.isEmpty) return;

  final notificationId = arguments[0] as String;

  final index = _notifications.indexWhere((n) => n.id == notificationId);
  if (index != -1) {
    // Update local state
    _notifications[index] = NotificationDto(
      id: _notifications[index].id,
      title: _notifications[index].title,
      message: _notifications[index].message,
      type: _notifications[index].type,
      isRead: true,
      createdOn: _notifications[index].createdOn,
      readAt: DateTime.now(),
      relatedEntityType: _notifications[index].relatedEntityType,
      relatedEntityId: _notifications[index].relatedEntityId,
    );
    notifyListeners();
  }
}

void _handleAllMarkedAsRead(List<Object>? arguments) {
  // Mark all as read in local state
  _notifications = _notifications.map((n) => NotificationDto(
    id: n.id,
    title: n.title,
    message: n.message,
    type: n.type,
    isRead: true,
    createdOn: n.createdOn,
    readAt: DateTime.now(),
    relatedEntityType: n.relatedEntityType,
    relatedEntityId: n.relatedEntityId,
  )).toList();

  _unreadCount = 0;
  notifyListeners();
}

void _handleDeleted(List<Object>? arguments) {
  if (arguments == null || arguments.isEmpty) return;

  final notificationId = arguments[0] as String;
  _notifications.removeWhere((n) => n.id == notificationId);
  notifyListeners();
}

void _handleError(List<Object>? arguments) {
  if (arguments == null || arguments.isEmpty) return;

  final error = arguments[0] as String;
  print('❌ Notification error: $error');
  // Show error to user
}
```

---

## Complete Service with ChangeNotifier

```dart
import 'package:flutter/foundation.dart';
import 'package:signalr_netcore/signalr_client.dart';

class NotificationService extends ChangeNotifier {
  HubConnection? _hubConnection;
  final String apiBaseUrl;
  String? _accessToken;

  List<NotificationDto> _notifications = [];
  int _unreadCount = 0;
  bool _isConnected = false;

  List<NotificationDto> get notifications => _notifications;
  int get unreadCount => _unreadCount;
  bool get isConnected => _isConnected;

  NotificationService({required this.apiBaseUrl});

  Future<void> connect(String accessToken) async {
    _accessToken = accessToken;

    _hubConnection = HubConnectionBuilder()
        .withUrl(
          '$apiBaseUrl/notifications?access_token=$accessToken',
          HttpConnectionOptions(
            skipNegotiation: true,
            transport: HttpTransportType.WebSockets,
          ),
        )
        .withAutomaticReconnect(
          retryDelays: [0, 2000, 5000, 10000, 30000],
        )
        .build();

    _setupEventHandlers();
    _setupConnectionHandlers();

    try {
      await _hubConnection!.start();
      _isConnected = true;
      notifyListeners();

      // Load initial data
      await getUnreadNotifications();
      await getUnreadCount();
    } catch (e) {
      print('❌ Failed to connect: $e');
      _isConnected = false;
      notifyListeners();
    }
  }

  void _setupConnectionHandlers() {
    _hubConnection!.onclose(({error}) {
      print('Connection closed: $error');
      _isConnected = false;
      notifyListeners();
    });

    _hubConnection!.onreconnecting(({error}) {
      print('Reconnecting...');
      _isConnected = false;
      notifyListeners();
    });

    _hubConnection!.onreconnected(({connectionId}) {
      print('✅ Reconnected');
      _isConnected = true;
      notifyListeners();

      // Refresh data after reconnection
      getUnreadNotifications();
    });
  }

  void _setupEventHandlers() {
    _hubConnection!.on('ReceiveNotification', _handleNewNotification);
    _hubConnection!.on('NotificationsLoaded', _handleNotificationsLoaded);
    _hubConnection!.on('UnreadCountUpdated', _handleUnreadCountUpdated);
    _hubConnection!.on('NotificationMarkedAsRead', _handleMarkedAsRead);
    _hubConnection!.on('AllNotificationsMarkedAsRead', _handleAllMarkedAsRead);
    _hubConnection!.on('NotificationDeleted', _handleDeleted);
    _hubConnection!.on('Error', _handleError);
  }

  // Event handlers as shown above...

  Future<void> getUnreadNotifications() async {
    await _hubConnection?.invoke('GetUnreadNotifications');
  }

  Future<void> markAsRead(String notificationId) async {
    await _hubConnection?.invoke('MarkAsRead', args: [notificationId]);
  }

  Future<void> markAllAsRead() async {
    await _hubConnection?.invoke('MarkAllAsRead');
  }

  Future<void> deleteNotification(String notificationId) async {
    await _hubConnection?.invoke('DeleteNotification', args: [notificationId]);
  }

  Future<void> getUnreadCount() async {
    await _hubConnection?.invoke('GetUnreadCount');
  }

  @override
  void dispose() {
    _hubConnection?.stop();
    super.dispose();
  }
}
```

---

## Provider Setup

In `main.dart`:

```dart
import 'package:provider/provider.dart';

void main() {
  runApp(
    MultiProvider(
      providers: [
        ChangeNotifierProvider(
          create: (_) => NotificationService(
            apiBaseUrl: 'https://your-api.com',
          ),
        ),
      ],
      child: MyApp(),
    ),
  );
}
```

---

## UI Integration

### Notification Badge

```dart
class NotificationBadge extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Consumer<NotificationService>(
      builder: (context, service, child) {
        return Badge(
          isLabelVisible: service.unreadCount > 0,
          label: Text('${service.unreadCount}'),
          child: IconButton(
            icon: Icon(Icons.notifications),
            onPressed: () {
              Navigator.pushNamed(context, '/notifications');
            },
          ),
        );
      },
    );
  }
}
```

### Notification List

```dart
class NotificationList extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Consumer<NotificationService>(
      builder: (context, service, child) {
        if (!service.isConnected) {
          return Center(child: Text('Connecting...'));
        }

        if (service.notifications.isEmpty) {
          return Center(child: Text('No notifications'));
        }

        return ListView.builder(
          itemCount: service.notifications.length,
          itemBuilder: (context, index) {
            final notification = service.notifications[index];
            return NotificationTile(notification: notification);
          },
        );
      },
    );
  }
}
```

### Notification Card

```dart
class NotificationTile extends StatelessWidget {
  final NotificationDto notification;

  const NotificationTile({required this.notification});

  @override
  Widget build(BuildContext context) {
    final service = context.read<NotificationService>();

    return Card(
      color: notification.isRead ? null : Colors.blue.shade50,
      child: ListTile(
        leading: _getIcon(notification.type),
        title: Text(
          notification.title,
          style: TextStyle(
            fontWeight: notification.isRead ? FontWeight.normal : FontWeight.bold,
          ),
        ),
        subtitle: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(notification.message),
            SizedBox(height: 4),
            Text(
              _formatDateTime(notification.createdOn),
              style: TextStyle(fontSize: 12, color: Colors.grey),
            ),
          ],
        ),
        trailing: PopupMenuButton(
          itemBuilder: (context) => [
            if (!notification.isRead)
              PopupMenuItem(
                child: Text('Mark as read'),
                onTap: () => service.markAsRead(notification.id),
              ),
            PopupMenuItem(
              child: Text('Delete'),
              onTap: () => service.deleteNotification(notification.id),
            ),
          ],
        ),
        onTap: () {
          if (!notification.isRead) {
            service.markAsRead(notification.id);
          }
          // Navigate to related entity if applicable
          if (notification.relatedEntityId != null) {
            _navigateToRelatedEntity(context, notification);
          }
        },
      ),
    );
  }

  Widget _getIcon(String type) {
    switch (type.toLowerCase()) {
      case 'success':
        return Icon(Icons.check_circle, color: Colors.green);
      case 'warning':
        return Icon(Icons.warning, color: Colors.orange);
      case 'error':
        return Icon(Icons.error, color: Colors.red);
      default:
        return Icon(Icons.info, color: Colors.blue);
    }
  }

  String _formatDateTime(DateTime dateTime) {
    final now = DateTime.now();
    final difference = now.difference(dateTime);

    if (difference.inMinutes < 1) return 'Just now';
    if (difference.inMinutes < 60) return '${difference.inMinutes}m ago';
    if (difference.inHours < 24) return '${difference.inHours}h ago';
    if (difference.inDays < 7) return '${difference.inDays}d ago';

    return '${dateTime.day}/${dateTime.month}/${dateTime.year}';
  }

  void _navigateToRelatedEntity(BuildContext context, NotificationDto notification) {
    if (notification.relatedEntityType == 'Complaint') {
      Navigator.pushNamed(
        context,
        '/complaint-details',
        arguments: notification.relatedEntityId,
      );
    }
  }
}
```

---

## Local Notifications (Push Notifications)

To show OS-level notifications when app is in background:

```yaml
dependencies:
  flutter_local_notifications: ^16.0.0
```

```dart
import 'package:flutter_local_notifications/flutter_local_notifications.dart';

class LocalNotificationService {
  static final FlutterLocalNotificationsPlugin _plugin =
      FlutterLocalNotificationsPlugin();

  static Future<void> initialize() async {
    const androidSettings = AndroidInitializationSettings('@mipmap/ic_launcher');
    const iosSettings = DarwinInitializationSettings();

    const settings = InitializationSettings(
      android: androidSettings,
      iOS: iosSettings,
    );

    await _plugin.initialize(settings);
  }

  static Future<void> showNotification(NotificationDto notification) async {
    const androidDetails = AndroidNotificationDetails(
      'complaints_channel',
      'Complaints',
      channelDescription: 'Complaint notifications',
      importance: Importance.high,
      priority: Priority.high,
    );

    const iosDetails = DarwinNotificationDetails();

    const details = NotificationDetails(
      android: androidDetails,
      iOS: iosDetails,
    );

    await _plugin.show(
      notification.id.hashCode,
      notification.title,
      notification.message,
      details,
    );
  }
}

// In NotificationService's _handleNewNotification:
void _handleNewNotification(List<Object>? arguments) {
  // ... existing code ...

  // Show local notification
  LocalNotificationService.showNotification(notification);
}
```

---

## Authentication Flow

```dart
// 1. Login and get token
final authService = AuthService();
final token = await authService.login(email, password);

// 2. Connect to notifications
final notificationService = context.read<NotificationService>();
await notificationService.connect(token);

// 3. On logout, disconnect
await notificationService.disconnect();
```

---

## Error Handling

```dart
try {
  await notificationService.markAsRead(notificationId);
} catch (e) {
  ScaffoldMessenger.of(context).showSnackBar(
    SnackBar(content: Text('Failed to mark as read: $e')),
  );
}
```

---

## Connection States

```dart
Consumer<NotificationService>(
  builder: (context, service, child) {
    if (service.isConnected) {
      return Icon(Icons.cloud_done, color: Colors.green);
    } else {
      return Icon(Icons.cloud_off, color: Colors.red);
    }
  },
)
```

---

## Testing

Use the WebSocket endpoint directly:

```
ws://localhost:5000/notifications?access_token=YOUR_TOKEN
```

---

## Important Notes

- **Authentication Required**: Connection will fail without valid JWT
- **UTC Timestamps**: All datetime fields are in UTC
- **Automatic Reconnect**: Enabled by default with exponential backoff
- **User-Scoped**: Users only receive their own notifications
- **Type Safety**: Use the provided data models for compile-time safety

---

## Troubleshooting

### Connection Fails

- Verify token is valid and not expired
- Check API URL is correct
- Ensure WebSocket protocol is allowed (ws:// or wss://)

### No Notifications Received

- Check connection status (`isConnected`)
- Verify event handlers are registered before calling `start()`
- Check server logs for errors

### Duplicate Notifications

- Ensure you're not creating multiple `NotificationService` instances
- Use Provider/Riverpod for singleton management

---

## Support

For issues or questions, refer to:

- [SignalR Flutter Package](https://pub.dev/packages/signalr_netcore)
- [ASP.NET Core SignalR Documentation](https://learn.microsoft.com/en-us/aspnet/core/signalr)
