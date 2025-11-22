# WebSocket API Documentation - CMS Notifications

## Overview

The CMS API provides real-time notifications via SignalR WebSockets. All notification operations (sending, retrieving, marking as read) are handled through WebSocket connections.

---

## Connection

### Endpoint

```
ws://localhost:5000/notifications
wss://localhost:5001/notifications (HTTPS)
```

### Authentication

**Required**: JWT Bearer token must be provided via query string

```javascript
const token = "your_jwt_token_here";
const connection = new signalR.HubConnectionBuilder()
  .withUrl(`https://localhost:5001/notifications?access_token=${token}`)
  .build();
```

---

## Client Setup (JavaScript/TypeScript)

### Installation

```bash
npm install @microsoft/signalr
```

### Basic Connection

```typescript
import * as signalR from "@microsoft/signalr";

const token = localStorage.getItem("accessToken");

const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${API_BASE_URL}/notifications?access_token=${token}`)
  .withAutomaticReconnect()
  .configureLogging(signalR.LogLevel.Information)
  .build();

// Start connection
await connection.start();
console.log("Connected to notifications hub");
```

---

## Client → Server Methods

These methods are invoked by the client to request data or perform actions.

### 1. Get Unread Notifications

```typescript
connection.invoke("GetUnreadNotifications");
// Server responds via "NotificationsLoaded" event
```

### 2. Get Recent Notifications

```typescript
connection.invoke("GetRecentNotifications", 50); // Get last 50 notifications
// Server responds via "NotificationsLoaded" event
```

### 3. Mark Notification as Read

```typescript
const notificationId = "guid-here";
connection.invoke("MarkAsRead", notificationId);
// Server responds via "NotificationMarkedAsRead" event
// Server also sends "UnreadCountUpdated" event
```

### 4. Mark All as Read

```typescript
connection.invoke("MarkAllAsRead");
// Server responds via "AllNotificationsMarkedAsRead" event
// Server also sends "UnreadCountUpdated" event with count = 0
```

### 5. Delete Notification

```typescript
const notificationId = "guid-here";
connection.invoke("DeleteNotification", notificationId);
// Server responds via "NotificationDeleted" event
// Server also sends "UnreadCountUpdated" event
```

### 6. Get Unread Count

```typescript
connection.invoke("GetUnreadCount");
// Server responds via "UnreadCountUpdated" event
```

---

## Server → Client Events

These events are sent by the server and must be registered as listeners.

### 1. ReceiveNotification

**Triggered**: When a new notification is created and sent to the user.

```typescript
connection.on("ReceiveNotification", (notification: NotificationDto) => {
  console.log("New notification:", notification);
  // Update UI: show toast, badge count, etc.
});
```

### 2. NotificationsLoaded

**Triggered**: Response to `GetUnreadNotifications()` or `GetRecentNotifications()`

```typescript
connection.on("NotificationsLoaded", (notifications: NotificationDto[]) => {
  console.log("Notifications loaded:", notifications);
  // Display notifications in UI
});
```

### 3. UnreadCountUpdated

**Triggered**: When unread count changes (after marking as read, new notification, etc.)

```typescript
connection.on("UnreadCountUpdated", (count: number) => {
  console.log("Unread count:", count);
  // Update badge UI
});
```

### 4. NotificationMarkedAsRead

**Triggered**: Confirmation that a notification was marked as read

```typescript
connection.on("NotificationMarkedAsRead", (notificationId: string) => {
  console.log("Notification marked as read:", notificationId);
  // Update UI to reflect read status
});
```

### 5. AllNotificationsMarkedAsRead

**Triggered**: Confirmation that all notifications were marked as read

```typescript
connection.on("AllNotificationsMarkedAsRead", () => {
  console.log("All notifications marked as read");
  // Update UI
});
```

### 6. NotificationDeleted

**Triggered**: Confirmation that a notification was deleted

```typescript
connection.on("NotificationDeleted", (notificationId: string) => {
  console.log("Notification deleted:", notificationId);
  // Remove from UI
});
```

### 7. Error

**Triggered**: When an error occurs during an operation

```typescript
connection.on("Error", (errorMessage: string) => {
  console.error("Error:", errorMessage);
  // Show error to user
});
```

---

## Data Structures

### NotificationDto

```typescript
interface NotificationDto {
  id: string; // GUID
  title: string; // Notification title
  message: string; // Notification message
  type: string; // "Info" | "Success" | "Warning" | "Error"
  isRead: boolean; // Read status
  createdOn: string; // ISO 8601 datetime
  readAt: string | null; // ISO 8601 datetime or null
  relatedEntityType: string | null; // e.g., "Complaint"
  relatedEntityId: string | null; // GUID of related entity
}
```

### Notification Types

- `Info` - General information
- `Success` - Positive confirmation (e.g., complaint resolved)
- `Warning` - Important notice
- `Error` - Error or failure notification

---

## Complete Example

```typescript
import * as signalR from "@microsoft/signalr";

class NotificationService {
  private connection: signalR.HubConnection;

  constructor(apiBaseUrl: string, token: string) {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/notifications?access_token=${token}`)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    this.setupEventHandlers();
  }

  private setupEventHandlers() {
    // Listen for new notifications
    this.connection.on(
      "ReceiveNotification",
      (notification: NotificationDto) => {
        this.showToast(notification);
        this.updateBadge();
      }
    );

    // Listen for notifications loaded
    this.connection.on(
      "NotificationsLoaded",
      (notifications: NotificationDto[]) => {
        this.displayNotifications(notifications);
      }
    );

    // Listen for unread count updates
    this.connection.on("UnreadCountUpdated", (count: number) => {
      this.updateBadgeCount(count);
    });

    // Listen for errors
    this.connection.on("Error", (error: string) => {
      console.error("Notification error:", error);
    });
  }

  async start() {
    try {
      await this.connection.start();
      console.log("✅ Connected to notifications hub");

      // Load initial unread notifications
      await this.connection.invoke("GetUnreadNotifications");

      // Get unread count
      await this.connection.invoke("GetUnreadCount");
    } catch (err) {
      console.error("❌ Connection failed:", err);
    }
  }

  async markAsRead(notificationId: string) {
    await this.connection.invoke("MarkAsRead", notificationId);
  }

  async markAllAsRead() {
    await this.connection.invoke("MarkAllAsRead");
  }

  async deleteNotification(notificationId: string) {
    await this.connection.invoke("DeleteNotification", notificationId);
  }

  async getRecentNotifications(count: number = 50) {
    await this.connection.invoke("GetRecentNotifications", count);
  }

  private showToast(notification: NotificationDto) {
    // Your toast implementation here
    console.log(`🔔 ${notification.type}: ${notification.title}`);
  }

  private updateBadgeCount(count: number) {
    // Update UI badge
    console.log(`Badge count: ${count}`);
  }

  private displayNotifications(notifications: NotificationDto[]) {
    // Display in UI
    console.log(`Loaded ${notifications.length} notifications`);
  }

  async stop() {
    await this.connection.stop();
  }
}

// Usage
const notificationService = new NotificationService(
  "https://localhost:5001",
  "your_jwt_token"
);

await notificationService.start();
```

---

## Connection Lifecycle

### On Connect

When a user connects to the hub, the server automatically:

1. Sends the current unread count via `UnreadCountUpdated`

### Auto-Reconnect

The SignalR client library automatically handles reconnections if the connection is lost. Configure with:

```typescript
.withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
```

### Handling Disconnects

```typescript
connection.onreconnecting((error) => {
  console.log("Reconnecting...", error);
  // Show "Connecting..." UI
});

connection.onreconnected((connectionId) => {
  console.log("Reconnected", connectionId);
  // Refresh notifications
  connection.invoke("GetUnreadNotifications");
});

connection.onclose((error) => {
  console.log("Connection closed", error);
  // Show offline UI
});
```

---

## React Hook Example

```typescript
import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";

interface NotificationDto {
  id: string;
  title: string;
  message: string;
  type: string;
  isRead: boolean;
  createdOn: string;
}

export function useNotifications(apiBaseUrl: string, token: string | null) {
  const [notifications, setNotifications] = useState<NotificationDto[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [connection, setConnection] = useState<signalR.HubConnection | null>(
    null
  );

  useEffect(() => {
    if (!token) return;

    const hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/notifications?access_token=${token}`)
      .withAutomaticReconnect()
      .build();

    // Event handlers
    hubConnection.on("ReceiveNotification", (notification: NotificationDto) => {
      setNotifications((prev) => [notification, ...prev]);
    });

    hubConnection.on("NotificationsLoaded", (data: NotificationDto[]) => {
      setNotifications(data);
    });

    hubConnection.on("UnreadCountUpdated", (count: number) => {
      setUnreadCount(count);
    });

    hubConnection
      .start()
      .then(() => {
        console.log("Connected to notifications");
        hubConnection.invoke("GetUnreadNotifications");
      })
      .catch((err) => console.error("Connection error:", err));

    setConnection(hubConnection);

    return () => {
      hubConnection.stop();
    };
  }, [apiBaseUrl, token]);

  const markAsRead = async (id: string) => {
    if (connection) {
      await connection.invoke("MarkAsRead", id);
    }
  };

  const markAllAsRead = async () => {
    if (connection) {
      await connection.invoke("MarkAllAsRead");
    }
  };

  return {
    notifications,
    unreadCount,
    markAsRead,
    markAllAsRead,
  };
}
```

---

## Error Handling

Always wrap SignalR invocations in try-catch:

```typescript
try {
  await connection.invoke("MarkAsRead", notificationId);
} catch (err) {
  console.error("Failed to mark as read:", err);
  // Show error to user
}
```

---

## Testing with Postman

1. **Get JWT Token** via REST API:

   ```
   POST http://localhost:5000/api/auth/login
   ```

2. **Connect to WebSocket**:

   ```
   ws://localhost:5000/notifications?access_token=YOUR_TOKEN
   ```

3. **Send Protocol Handshake**:

   ```json
   { "protocol": "json", "version": 1 }
   ```

4. **Invoke Methods**:
   ```json
   {
     "type": 1,
     "target": "GetUnreadNotifications",
     "arguments": []
   }
   ```

---

## Notes

- All datetime fields are in **UTC ISO 8601** format
- Notification IDs are **GUIDs**
- The hub requires **authentication** - unauthenticated connections are rejected
- Notifications are **user-scoped** - users only see their own notifications
- The connection uses **JSON Hub Protocol** (default for SignalR)

---

## Support

For issues or questions, contact the backend team or refer to the [SignalR documentation](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client).
