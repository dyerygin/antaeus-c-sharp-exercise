# CSharpApp.UpstreamMock

A fake of the original Platzi Fake Store API contract, written as a small .NET 10 Minimal API.

The public service the task names (`https://api.escuelajs.co/api/v1/`) is offline, so this project stands in for it.
It is a working fake, not a stub: it keeps state in memory, issues real JWTs and validates requests.
It implements only the subset this solution uses.
See the repository README, section "Upstream and mock", for why it exists, and ADR-0003 in `docs/adr/`.

It references none of the application projects.

## Contract sources

- the task README and the supplied Postman/Insomnia collections;
- the original backend source (`PlatziLabs/fake-api-backend`, commit `12bbc93`);
- behaviour measured by running that backend locally (status codes, error bodies, token lifetimes, price storage).

## Endpoints

All routes are under `/api/v1`, except the health endpoint.

| Method and route | Success | Errors |
| --- | --- | --- |
| `POST auth/login` body `{"email","password"}` | 201 `{"access_token","refresh_token"}` | 401 `{"message":"Unauthorized","statusCode":401}` |
| `POST auth/refresh-token` body `{"refreshToken"}` | 201 with a new token pair | 401 `{"message":"Invalid","error":"Unauthorized","statusCode":401}` |
| `GET auth/profile` | 200 `{"id","email","name","role","avatar"}` (no password) | 401 without a valid bearer token |
| `GET products?offset&limit` | 200 array; without `limit`, all products (as the original) | 401 when tokens are required and missing |
| `GET products/{id}` | 200 | 400 `EntityNotFoundError` for an unknown id; 400 validation for a non-numeric id |
| `POST products` | 201, echoing the request | 400 validation; 400 `EntityNotFoundError` for an unknown `categoryId` |
| `GET categories?limit` | 200 array | 401 when tokens are required and missing |
| `GET categories/{id}` | 200 | 400 `EntityNotFoundError` for an unknown id; 400 validation for a non-numeric id |
| `POST categories` body `{"name","image"}` | 201 | 400 validation |
| `GET /health/live` | 200 | — |

### Error bodies

The mock returns the original's error shapes, because the API under test parses them:

- **Unknown id** → `400` with `{"path","timestamp","name":"EntityNotFoundError","message"}`.
  The original backend reports "not found" this way (a TypeORM error), not as 404. The API maps it to its own 404.
- **Validation** → `400` with `{"message":[…],"error":"Bad Request","statusCode":400}`.
  `message` is an array of field messages.
- **Auth** → `401` with the bodies in the table above.

Product create validation mirrors the original: `title` and `description` not empty, `price` positive,
`categoryId` a number, `images` an array of at least one URL. The original's content moderation is not reproduced.

### Price storage quirk

Create echoes the request price, but the store keeps the integer part, as the original does on its integer column:

- `POST products` with `"price": 10.5` → `201` with `"price": 10.5`;
- `GET products/{id}` afterwards → `"price": 10`.

The API under test handles this by reading the product back after create.

### Tokens

- Tokens are JWTs signed with `MockAuth:SigningKey`; the access token carries an `exp` claim.
- `auth/profile` always requires a bearer token, as in the original.
- Products and categories require a token only when `MockAuth:RequireTokenOnResources` is true.
  The original never requires one there, so the default (`false`) matches it. Setting it to `true` exercises the
  API's token handling (cache, refresh, one retry on 401).
- The refresh token lives 10 hours, as in the original. An invalid, expired or tampered token, or an access token sent
  as a refresh token, returns 401.

## Seed data

In memory, reset on every restart:

- the three users of the original seed, including `john@mail.com` / `changeme` (public demo credentials);
- 5 categories;
- 10 products, all with integer prices.

The seed is `seed.json`, embedded in the assembly.

## Settings

| Setting | Default | Meaning |
| --- | --- | --- |
| `MockAuth:RequireTokenOnResources` | `false` | require a bearer token on products and categories |
| `MockAuth:AccessTokenMinutes` | `28800` (20 days, as the original) | access token lifetime |
| `MockAuth:SigningKey` | a demo value in `appsettings.json` | HMAC key for the JWTs (at least 32 characters); a demo value, not a secret |

The settings are validated on start.

Environment variables use `__` instead of `:`, for example `MockAuth__RequireTokenOnResources=true`.

## Run

Locally, on `http://localhost:5081` (the API's Development settings point there):

```bash
dotnet run --project mocks/CSharpApp.UpstreamMock
```

With tokens required on resources and short-lived tokens (bash, then PowerShell):

```bash
MockAuth__RequireTokenOnResources=true MockAuth__AccessTokenMinutes=1 dotnet run --project mocks/CSharpApp.UpstreamMock
```

```powershell
$env:MockAuth__RequireTokenOnResources = "true"; $env:MockAuth__AccessTokenMinutes = "1"
dotnet run --project mocks/CSharpApp.UpstreamMock
```

A quick check:

```bash
curl -s -X POST http://localhost:5081/api/v1/auth/login \
  -H "Content-Type: application/json" -d '{"email":"john@mail.com","password":"changeme"}'
curl -s "http://localhost:5081/api/v1/products?limit=2"
```

## Tests

```bash
dotnet test --project tests/CSharpApp.UpstreamMock.Tests
```

The contract tests cover login, refresh, profile, the token switch, the `EntityNotFoundError` response, validation
errors and the price storage quirk.

## Not covered

- update and delete;
- product filters other than `offset` and `limit` (`title`, `price`, `price_min`, `price_max`, `categoryId`,
  `categorySlug`);
- users, files and other endpoints of the original;
- content moderation;
- persistence across restarts.
