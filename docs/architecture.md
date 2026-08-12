# Architecture

The application will use a layered monolith:

```text
React
  -> ASP.NET Core API
  -> Application commands and queries
  -> Domain and infrastructure
  -> MongoDB
```

Detailed component responsibilities will be recorded as each vertical slice is implemented.
