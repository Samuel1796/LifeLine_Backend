# Nook API (LifeLine Backend)

ASP.NET Core (.NET 10) API for Nook, a workspace booking app. Employees browse offices and book desks/rooms; managers create and edit spaces, see all bookings and view occupancy. Booking changes are pushed live over SignalR.

Frontend: [LifeLine_Frontend](https://github.com/Samuel1796/LifeLine_Frontend)

## Stack

- ASP.NET Core 10, controllers + SignalR
- PostgreSQL via EF Core (Npgsql)
- JWT bearer authentication with `Employee` / `Manager` roles

## Running locally

Requirements: .NET 10 SDK and a PostgreSQL server.

```bash
dotnet run
```

The API listens on `http://localhost:5000`. In Development it falls back to the `ConnectionStrings:Default` in `appsettings.json` (`Host=localhost;Port=5432;Database=nook;Username=postgres;Password=postgres`). To point somewhere else, create a `.env` file in the project root (it is git- and docker-ignored):

```
DATABASE_URL=postgresql://user:password@host:5432/dbname
```

On startup the app creates the `nook` schema and its tables if they are missing, then seeds demo data. There are no EF migrations.

### Demo accounts

All seeded users have the password `Password123!`.

| Email | Role |
|---|---|
| `manager@demo.com` | Manager |
| `ama@demo.com`, `kwame@demo.com`, `esi@demo.com`, `kofi@demo.com` | Employee |

## Configuration

| Variable | Required | Description |
|---|---|---|
| `DATABASE_URL` | yes (prod) | `postgresql://user:pass@host[:port]/db`. TLS is required automatically for non-local hosts unless the URL sets `sslmode`. |
| `ConnectionStrings__Default` | no | Npgsql key=value string or URL. **Takes precedence over `DATABASE_URL`** if both are set. |
| `FRONTEND_ORIGINS` | yes (prod) | Comma-separated allowed CORS origins, no trailing slash, e.g. `https://nook.vercel.app`. Defaults to `http://localhost:5173`. |
| `Jwt__Key` | yes (prod) | JWT signing key (32+ chars). The value in `appsettings.json` is for development only. |
| `Jwt__Issuer`, `Jwt__Audience` | no | Default to `Nook` / `NookClient`. |
| `PORT` | no | Set by the host; the app binds `0.0.0.0:$PORT` when present. |

## Deploying to Render

1. Create a **PostgreSQL** instance. Free instances expire after 30 days.
2. Create a **Web Service** from this repo using the **Docker** runtime (the `Dockerfile` is in the root).
3. Set the environment variables above. Use the database's **External Database URL** for `DATABASE_URL`, or the **Internal Database URL** only if the web service is in the same region as the database.
4. Remove any stale `ConnectionStrings__Default`, since it overrides `DATABASE_URL`.

On boot the log prints `Using PostgreSQL host ...`. If the database can't be reached the app retries 5 times and then exits with a one-line reason.

### Troubleshooting

- **`Attempted to read past the end of the stream`**: Render's proxy closed the connection before Postgres answered. The database is usually deleted, suspended/expired, or blocked by its Access Control IP allow list.
- **Hostname could not be resolved**: you're using an internal hostname from outside its region. Use the external URL.
- **CORS errors in the browser**: `FRONTEND_ORIGINS` doesn't exactly match the frontend's origin.

## API

All routes except auth require `Authorization: Bearer <token>`.

| Method | Route | Access |
|---|---|---|
| POST | `/api/auth/register` | public |
| POST | `/api/auth/login` | public |
| GET | `/api/offices` | any user |
| GET | `/api/spaces` | any user |
| GET | `/api/spaces/{id}` | any user |
| POST | `/api/spaces` | Manager |
| PUT | `/api/spaces/{id}` | Manager |
| GET | `/api/spaces/occupancy` | Manager |
| POST | `/api/bookings` | any user |
| GET | `/api/bookings/mine` | any user |
| GET | `/api/bookings/all` | Manager |
| PATCH | `/api/bookings/{id}/cancel` | any user |

Errors are returned as `{ "error": "message" }`.

**SignalR hub:** `/hubs/notifications`. Pass the JWT as the `access_token` query parameter. The hub broadcasts `spaceBooked` and `bookingCancelled`.

## Project layout

```
Controllers/   HTTP endpoints
Hubs/          SignalR notification hub
Data/          AppDbContext and demo seeder
Models/        EF entities (User, Office, Workspace, Booking)
Dtos/          Request/response shapes
Services/      JWT token creation, local-day helpers
Program.cs     Configuration, DB connection resolution and startup
```
