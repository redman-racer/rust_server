# rust_server

## Plugin secrets

Tracked plugin configs can reference centralized secrets using `${VARIABLE_NAME}`.
Put the real values in `oxide/config/Secrets.local.json`; that file is ignored by
Git. Use `oxide/config/Secrets.example.json` as the starting shape.

Example:

```json
{
  "SharedSecret": "${WEBSITE_VIP_SHARED_SECRET}"
}
```

The plugins still accept plain values, but variable references keep API keys,
webhooks, and shared secrets out of version control.
