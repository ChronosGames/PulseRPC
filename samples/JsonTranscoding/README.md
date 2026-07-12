# Explicit JSON gateway

PulseRPC does not currently provide automatic JSON-to-wire transcoding. This maintained sample shows the supported alternative: one `IMyFirstHub` implementation is exposed through PulseRPC on TCP port `5010` and through explicit ASP.NET JSON endpoints.

Run the server:

```bash
dotnet run --project JsonTranscodingSample.Server/JsonTranscodingSample.Server.csproj
```

Then call `GET /api/hello?name=Alice&age=20` or `POST /api/users`. The JSON gateway is application code, so authentication, validation, HTTP status mapping, and versioning remain explicit rather than being inferred from the PulseRPC wire contract.
