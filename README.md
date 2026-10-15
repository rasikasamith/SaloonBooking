# Studio Sunday — Salon booking system

A React/Vite salon app and three .NET minimal API services backed by Microsoft SQL Server.

## What’s included

- **Customer booking flow:** choose a service and a time, then book an appointment.
- **Salon workspace:** today’s schedule, appointment list and status updates, customer list, and service prices.
- **Service management:** add a service with its category, duration, and price; it is saved through the Catalog API to SQL Server.
- **Microservices:** Auth (`/api/auth/login`), Catalog (`/api/services`), and Appointments (`/api/appointments` and `/api/appointments/slots`).

The service menu, homepage popular-services list, and booking price preview load the live catalog from `http://localhost:5141/api/services`. If the Catalog API is unavailable, the UI falls back to sample prices and shows that status. Appointment lists and customer lists still use sample dashboard data; they are not yet wired to their APIs. Reporting has been removed from both the React frontend and backend.

## Database setup

Run `database/init.sql` in SQL Server Management Studio against the SQL Server instance. It creates the `Users` table and seeds the sample Customer and Admin accounts as salted PBKDF2 password hashes. Configure credentials through environment variables; do not commit passwords to source control:

```powershell
$env:ConnectionStrings__Saloon='Server=DESKTOP-3AMT40D;Database=saloon-app-db;User Id=sa;Password=<your-password>;TrustServerCertificate=True;'
```

For a local instance that requires SQL Server authentication, supply the password in your own terminal session. Rotate any password that has been shared in chat before deploying this app.

## Run locally

Install frontend packages and start Vite:

```powershell
cd frontend
npm install
npm run dev
```

In separate terminals, set the connection string above and run each API:

```powershell
dotnet run --project backend/Saloon.Catalog --urls http://localhost:5141
dotnet run --project backend/Saloon.Appointments --urls http://localhost:5142
dotnet run --project backend/Saloon.Auth --urls http://localhost:5143
```

The APIs allow the Vite origin `http://localhost:5173`. Each service has a `/health` endpoint.

## Sample sign-in

Run the SQL setup script before starting Auth. The frontend starts at a sign-in screen and verifies these sample accounts through the Auth API:

- Customer: `myuser` / `pw123`
- Salon Admin: `myadmin` / `pw123`

The Auth API checks the password against the stored hash and returns the account role for the frontend to choose a view. This sample does not issue access tokens or protect the other APIs by role yet; add token-based authentication and authorization before using it beyond a local demonstration. Customer bookings are submitted to the Appointments API and saved in `dbo.Appointments`.

## API notes

- Available-slot query: `GET /api/appointments/slots?date=2026-10-14&serviceId=1` (30-minute starts, 9 AM–6 PM).
- Create appointment: `POST /api/appointments` with `{ "customerName": "...", "phone": "...", "serviceId": 1, "startAt": "2026-10-14T10:00:00" }`.
- Edit appointment: `PUT /api/appointments/{id}` with the same appointment fields; the API checks for schedule conflicts.
- Delete appointment: `DELETE /api/appointments/{id}`.
- Update status: `PATCH /api/appointments/{id}/status` with `{ "status": "Completed" }`.
"# SaloonBooking" 
