# Firebase push notifications

Firebase Cloud Messaging is disabled by default. Inventory movements continue normally
when Firebase is disabled, there are no registered tokens, credentials cannot be
resolved, or Firebase returns an error.

Configure Firebase through appsettings or environment variables. To use a local service
account JSON explicitly:

```text
Firebase__Enabled=true
Firebase__ProjectId=your-firebase-project-id
Firebase__CredentialsPath=C:\secure\firebase-service-account.json
```

To use Application Default Credentials, leave the credentials path empty:

```text
Firebase__Enabled=true
Firebase__ProjectId=your-firebase-project-id
Firebase__CredentialsPath=
```

ADC can resolve credentials from the environment, including
`GOOGLE_APPLICATION_CREDENTIALS`, local Google Cloud tooling, or the runtime identity
of a supported Google Cloud environment. Keep service account JSON files outside the
repository.

The backend initializes the Firebase Admin SDK lazily on the first alert send. Missing
or invalid credentials therefore do not prevent application startup, and send failures
remain best-effort.

Stock alerts are sent after an inventory movement is committed and only when severity
increases:

- normal to low stock
- normal to out of stock
- low stock to out of stock
