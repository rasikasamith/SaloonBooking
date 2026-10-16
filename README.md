# Studio Sunday — Salon booking system

React/Vite customer and salon-owner application backed by three .NET 10 APIs and SQL Server.

## Authentication and authorization

- Customers can create accounts at `/api/auth/register`. Passwords must be at least 12 characters and include letters and numbers; they are stored as salted PBKDF2 hashes.
- Login returns a signed JWT with the account ID, name, and role. Tokens expire after 60 minutes by default and remain in browser memory only; refreshing the page signs the user out.
- Login attempts are limited to 10 per IP per minute. Registration attempts are limited to 5 per IP per hour.
- Catalog reads and appointment slot availability are public. Appointment creation requires a Customer or Admin token. Reading, editing, deleting, or changing status on appointments, and changing services, requires an Admin token.
- Configure the same strong JWT signing key, issuer, and audience in Auth, Catalog, and Appointments. Never commit a signing key or database password.

## Database setup

Run `database/init.sql` in SQL Server Management Studio. It creates the service, appointment, and user tables and seeds service prices, but does not create login accounts. For local development only, run `database/dev-users.sql` to add `myuser` and `myadmin`, both with the development password `pw123`. **Never run `dev-users.sql` in production.** For production, create a real customer account through the registration screen, then grant the intended owner the Admin role through a controlled database administration process.

For local SQL Server, provide the connection string through an environment variable in each API terminal. Do not put real credentials in appsettings files or Git:

```powershell
$env:ConnectionStrings__Saloon='Server=DESKTOP-3AMT40D;Database=saloon-app-db;User Id=sa;Password=<your-local-password>;TrustServerCertificate=True;'
```

## Local JWT secret

In PowerShell, create one random signing key and store it in each API project's .NET user-secrets store. User secrets stay outside the project directory:

```powershell
$jwtKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
$projects = @('backend/Saloon.Auth', 'backend/Saloon.Catalog', 'backend/Saloon.Appointments')
foreach ($project in $projects) {
    dotnet user-secrets init --project $project
    dotnet user-secrets set 'Jwt:SigningKey' $jwtKey --project $project
}
Remove-Variable jwtKey
```

Run the user-secrets initialization once per project. Reuse the same key in all three API projects. The defaults `Issuer=Saloon.Auth` and `Audience=Saloon.Api` match between the services.

## Run locally

In separate terminals, start the APIs (set the SQL connection string in each terminal first):

```powershell
dotnet run --project backend/Saloon.Catalog --urls http://localhost:5141
dotnet run --project backend/Saloon.Appointments --urls http://localhost:5142
dotnet run --project backend/Saloon.Auth --urls http://localhost:5143
```

Then start the frontend:

```powershell
cd frontend
npm install
npm run dev
```

The development frontend uses the API addresses on localhost ports 5141–5143. Each API exposes `/health`. The browser permits Vite at `http://localhost:5173` or `http://localhost:5174` by default.

## API access

| Endpoint | Access |
| --- | --- |
| `POST /api/auth/login` | Anonymous, rate limited |
| `POST /api/auth/register` | Anonymous customer registration, rate limited |
| `GET /api/services` | Anonymous |
| `POST`, `PUT`, `DELETE /api/services` | Admin bearer token |
| `GET /api/appointments/slots` | Anonymous |
| `POST /api/appointments` | Customer or Admin bearer token |
| `GET`, `PUT`, `DELETE /api/appointments` and `PATCH /api/appointments/{id}/status` | Admin bearer token |

The frontend sends the login token in the `Authorization: Bearer <token>` header for protected API calls. In production, deploy over HTTPS, set the same strong `Jwt__SigningKey` secret in each API, and configure `Cors__AllowedOrigins__0` to the exact HTTPS frontend origin. Do not use SQL Server's `sa` account for the deployed services; use a restricted database identity or managed identity with the minimum required permissions.

Before publishing, do not copy the local development database as-is: it may already contain the known `myadmin` / `pw123` account. In the production database, deactivate any development accounts, register the real salon-owner account, then promote only that chosen account to Admin:

```sql
UPDATE dbo.Users SET IsActive = 0 WHERE Username IN (N'myuser', N'myadmin');
-- After the owner registers, replace the value below with that exact username:
UPDATE dbo.Users SET Role = N'Admin' WHERE Username = N'<owner-username>';
```

This is a baseline. Email verification, password reset/recovery, and account/session revocation are not implemented yet and should be added before relying on the app for customer account recovery or long-term sessions.
