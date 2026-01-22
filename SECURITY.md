# Security Configuration Guide

This document outlines how to securely configure sensitive credentials for the OnlineContract application.

## Important Security Practices

**NEVER commit actual credentials, API keys, or secrets to version control.**

The configuration files (`appsettings.json`, `appsettings.Development.json`) contain empty string placeholders for sensitive values to document what configuration is required. These files should remain with empty values in source control.

## Configuration Methods

### Option 1: User Secrets (Recommended for Development)

User secrets provide a secure way to store sensitive configuration during local development without risk of accidental commits.

Initialize user secrets for the project:

```bash
cd /path/to/OnlineContract
dotnet user-secrets init
```

Set payment provider credentials:

```bash
# WSPay payment provider
dotnet user-secrets set "Payments:WSPay:MerchantId" "your-merchant-id"
dotnet user-secrets set "Payments:WSPay:StoreId" "your-store-id"
dotnet user-secrets set "Payments:WSPay:ApiKey" "your-api-key"
dotnet user-secrets set "Payments:WSPay:ApiSecret" "your-api-secret"
dotnet user-secrets set "Payments:WSPay:WebhookSecret" "your-webhook-secret"
```

Set SMTP credentials:

```bash
dotnet user-secrets set "Smtp:Pass" "your-smtp-password"
```

Set database connection (if needed):

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;Database=...;User Id=...;Password=..."
```

### Option 2: Environment Variables (Recommended for Production)

Environment variables are ideal for production deployments, especially in containerized or cloud environments.

Configure environment variables in your deployment environment:

```bash
# Linux/macOS
export Payments__WSPay__MerchantId="your-merchant-id"
export Payments__WSPay__StoreId="your-store-id"
export Payments__WSPay__ApiKey="your-api-key"
export Payments__WSPay__ApiSecret="your-api-secret"
export Payments__WSPay__WebhookSecret="your-webhook-secret"
export Smtp__Pass="your-smtp-password"
export ConnectionStrings__DefaultConnection="Server=...;..."
```

```powershell
# Windows PowerShell
$env:Payments__WSPay__MerchantId = "your-merchant-id"
$env:Payments__WSPay__StoreId = "your-store-id"
$env:Payments__WSPay__ApiKey = "your-api-key"
$env:Payments__WSPay__ApiSecret = "your-api-secret"
$env:Payments__WSPay__WebhookSecret = "your-webhook-secret"
$env:Smtp__Pass = "your-smtp-password"
$env:ConnectionStrings__DefaultConnection = "Server=...;..."
```

**Note:** Environment variables use double underscores (`__`) instead of colons (`:`) for nested configuration keys.

### Option 3: Azure Key Vault (Recommended for Enterprise Production)

For enterprise deployments on Azure, use Azure Key Vault for centralized secret management:

1. Create an Azure Key Vault
2. Add your secrets to the vault
3. Configure your application to read from Key Vault
4. Use Managed Identity for secure, credential-less access

Refer to [Azure Key Vault configuration documentation](https://learn.microsoft.com/en-us/aspnet/core/security/key-vault-configuration) for implementation details.

## Required Configuration Values

### Payment Provider (WSPay)

- `Payments:WSPay:MerchantId` - Your WSPay merchant identifier
- `Payments:WSPay:StoreId` - Your WSPay store identifier  
- `Payments:WSPay:ApiKey` - WSPay API key for authentication
- `Payments:WSPay:ApiSecret` - WSPay API secret for request signing
- `Payments:WSPay:WebhookSecret` - Secret for validating webhook callbacks
- `Payments:WSPay:Environment` - Set to "Testing", "Sandbox", or "Production"

### SMTP Email

- `Smtp:Host` - SMTP server hostname (e.g., smtp.gmail.com)
- `Smtp:Port` - SMTP server port (e.g., 587 for TLS, 465 for SSL)
- `Smtp:User` - SMTP username/email address
- `Smtp:Pass` - SMTP password or app-specific password
- `Smtp:From` - Email address to use in the "From" field

### Database Connection

- `ConnectionStrings:DefaultConnection` - Database connection string with credentials

## Configuration Priority

ASP.NET Core loads configuration in the following order (later sources override earlier ones):

1. appsettings.json
2. appsettings.{Environment}.json
3. User Secrets (Development environment only)
4. Environment Variables
5. Command-line arguments

This means environment variables and user secrets will override values in configuration files.

## Verification

To verify your secrets are configured correctly without exposing them:

```bash
dotnet run --project OnlineContract.csproj
```

Check the application logs for any configuration-related warnings or errors. The application will log errors if required credentials are missing when features are used.

## Security Checklist

- [ ] Never commit actual credentials to source control
- [ ] Use `.gitignore` to exclude files containing secrets
- [ ] Rotate credentials regularly
- [ ] Use different credentials for each environment (dev, staging, production)
- [ ] Limit access to production credentials to authorized personnel only
- [ ] Use encryption for credentials at rest and in transit
- [ ] Monitor and audit credential usage
- [ ] Revoke and rotate credentials immediately if compromised

## Reporting Security Issues

If you discover a security vulnerability, please email security@example.com (replace with actual contact). Do not create public GitHub issues for security vulnerabilities.
