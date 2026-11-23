# Five-minute demo

A walkthrough for a reviewer. Each step has the exact command and the result to expect.
It runs against the Docker stack: the API on `localhost:8080` and the upstream mock on `localhost:8081`, with the mock
requiring a bearer token on every resource and issuing 5-minute access tokens. `jq` is used to shorten output.

## Shell notes

Commands are written for bash. In PowerShell:

- use `curl.exe`, not `curl` (in Windows PowerShell 5.1 `curl` is an alias for `Invoke-WebRequest`);
- replace `$API_KEY` with `$env:API_KEY`;
- pipe JSON bodies through stdin to avoid quoting problems:

  ```powershell
  '{"name":"Demo","image":"https://example.com/demo.png"}' |
    curl.exe -i -X POST http://localhost:8080/api/v1/categories `
      -H "X-Api-Key: $env:API_KEY" -H "Content-Type: application/json" --data-binary '@-'
  ```

## 1. Start the stack

bash:

```bash
cp .env.example .env
docker compose up --build -d --wait
export API_KEY="$(grep '^API_KEY=' .env | cut -d= -f2-)"
```

PowerShell:

```powershell
Copy-Item .env.example .env
docker compose up --build -d --wait
$env:API_KEY = (Select-String -Path .env -Pattern '^API_KEY=(.*)$').Matches[0].Groups[1].Value
```

Expected: `docker compose up` returns once both containers are healthy (`Container csharpapp-api-1 Healthy`);
`docker compose ps` shows `api` and `upstream-mock` as `(healthy)`.

## 2. List products (default page of 20)

```bash
curl -s http://localhost:8080/api/v1/products | jq length
curl -s "http://localhost:8080/api/v1/products?limit=101"
```

Expected: `10` (the mock seeds 10 products, fewer than the default page of 20). The second call returns 400
ProblemDetails with `"errors":{"limit":["The field limit must be between 1 and 100."]}` and a `traceId`.

## 3. Create without an API key

```bash
curl -i -X POST http://localhost:8080/api/v1/products \
  -H "Content-Type: application/json" \
  -d '{"title":"Demo","price":10.5,"description":"Demo product","categoryId":1,"images":["https://example.com/a.png"]}'
```

Expected: `401 Unauthorized`, `WWW-Authenticate: ApiKey`, `Content-Type: application/problem+json`. The request never
reaches the upstream.

## 4. Create a product: the stored price comes back

```bash
curl -i -X POST http://localhost:8080/api/v1/products \
  -H "X-Api-Key: $API_KEY" -H "Idempotency-Key: demo-001" -H "Content-Type: application/json" \
  -d '{"title":"Demo","price":10.5,"description":"Demo product","categoryId":1,"images":["https://example.com/a.png"]}'
```

Expected: `201 Created`, `Location: http://localhost:8080/api/v1/products/11`, and `"price":10`, not `10.5`.
The upstream echoes the request price but stores an integer; the API reads the product back and returns what was
stored.

## 5. Same `Idempotency-Key`, same body: replay

Run the command of step 4 again, unchanged.

Expected: the same `201`, `Location` and body, plus `Idempotent-Replayed: true`. No second product is created:

```bash
curl -s "http://localhost:8080/api/v1/products?limit=100" | jq '[.[] | select(.title=="Demo")] | length'
```

returns `1`.

## 6. Same key, different body

```bash
curl -i -X POST http://localhost:8080/api/v1/products \
  -H "X-Api-Key: $API_KEY" -H "Idempotency-Key: demo-001" -H "Content-Type: application/json" \
  -d '{"title":"Other","price":5,"description":"Other product","categoryId":1,"images":["https://example.com/b.png"]}'
```

Expected: `422 Unprocessable Entity`, ProblemDetails titled "This Idempotency-Key was already used with a different
request body."

## 7. Upstream token expiry and refresh

The mock in Docker issues 5-minute access tokens. The API renews its token min(60 s, 10 % of the lifetime) before
`exp`, so here 30 s early: a call made more than 4.5 minutes after the first one triggers a refresh.

```bash
sleep 280
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8080/api/v1/categories
docker compose logs api | grep -oE 'Upstream [^,]*auth/[^,]* ms'
```

Expected: `200`, and the API log shows the first `auth/login` and then one `auth/refresh-token`, each as an upstream
timing line (`Upstream \"auth\" \"POST\" \"/api/v1/auth/refresh-token\" -> 201 in 6 ms`; the logs are compact JSON,
so values appear with escaped quotes). No token value appears in the log.

To shorten the wait, run steps 9 and 10 first and come back to this one.

## 8. Upstream down

```bash
docker compose stop upstream-mock
curl -s -o /dev/null -w "%{http_code} %{time_total}s\n" http://localhost:8080/api/v1/products
sleep 11
curl -s -w "\n%{http_code}\n" http://localhost:8080/health/ready
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8080/health/live
docker compose start upstream-mock
```

Expected: the GET returns `502` within a fraction of a second (connection refused; a hung upstream would give `504`
after the 10 s total timeout). Once the 10 s readiness cache has expired, `/health/ready` returns `503` with the
`upstream` check `Unhealthy`; `/health/live` stays `200`.

## 9. Timing logs

```bash
docker compose logs api | grep -oE '(Upstream|HTTP) [^,]* ms' | tail -n 4
```

Expected: one upstream line per upstream call and one inbound line per request, for example
`Upstream \"categories\" \"GET\" \"/api/v1/categories\" -> 200 in 16.4 ms` and
`HTTP \"GET\" \"/api/v1/categories\" -> 200 in 31.9 ms` (compact JSON, so the quotes are escaped). The container
health checks show up as `HTTP \"GET\" \"/health/live\"` lines as well. No query strings or headers are logged;
slow upstream calls (1 s or more) are warnings. Each upstream call also increments the counter
`csharpapp.upstream.requests`.

## 10. OpenAPI

```bash
curl -s http://localhost:8080/openapi/v1.json | jq -c '.openapi, (.paths | keys)'
curl -s http://localhost:8080/openapi/v1.yaml | head -n 3
```

Expected: `"3.1.1"` and `["/api/v1/categories","/api/v1/categories/{id}","/api/v1/products","/api/v1/products/{id}"]`;
the YAML starts with `openapi: '3.1.1'`. The POST operations declare the `ApiKey` security scheme and the
`Idempotency-Key` header (maximum length 100).

## Clean up

```bash
docker compose down
```
