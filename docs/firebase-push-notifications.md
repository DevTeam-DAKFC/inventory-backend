# Firebase push notifications

Firebase Cloud Messaging is disabled by default. Inventory movements continue normally
when Firebase is disabled, credentials are not configured, there are no registered
tokens, or Firebase returns an error.

Configure Firebase through appsettings or environment variables:

```text
Firebase__Enabled=true
Firebase__ProjectId=your-firebase-project-id
Firebase__CredentialsPath=C:\secure\firebase-service-account.json
```

Keep the service account JSON outside the repository. The backend initializes the
Firebase Admin SDK lazily on the first alert send.

Stock alerts are sent after an inventory movement is committed and only when severity
increases:

- normal to low stock
- normal to out of stock
- low stock to out of stock
