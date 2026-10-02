# Tjidde.Logging — Wiki (Nederlands)

## Inhoudsopgave

1. [Introductie](#introductie)
2. [Installatie](#installatie)
3. [Snel aan de slag](#snel-aan-de-slag)
4. [Loguitvoer formaat](#loguitvoer-formaat)
5. [Configuratie-opties](#configuratie-opties)
6. [Klantcontext](#klantcontext)
7. [Maskering van gevoelige gegevens](#maskering-van-gevoelige-gegevens)
8. [Uitzonderingsopmaak](#uitzonderingsopmaak)
9. [Scopes](#scopes)
10. [Metrics-regels](#metrics-regels)
11. [CI/CD (GitHub Actions)](#cicd-github-actions)
12. [Toekomstige ontwikkelingen](#toekomstige-ontwikkelingen)

---

## Introductie

**Tjidde.Logging** is een gestructureerde, opinionated logging-bibliotheek gebouwd bovenop `Microsoft.Extensions.Logging`. Het produceert consistente, éénregelige loguitvoer die eenvoudig leesbaar is in consoles en logaggregators. Belangrijkste kenmerken:

- Gestandaardiseerd logformaat met tijdstempel, logniveau, klassenaam, methodenaam en klantcontext.
- Automatische maskering van gevoelige gegevens (wachtwoorden, tokens, API-sleutels, enz.).
- Compacte éénregelige uitzonderingsopmaak inclusief inner exceptions en stack traces.
- Klantcontextpropagatie via `AsyncLocal` — veilig voor async/await en multi-tenant scenario's.
- Scope-ondersteuning voor gestructureerde logging.
- `LogMetrics` voor metric-achtige regels, weergegeven als `[METRICS]` naast reguliere logberichten.
- Ondersteunt .NET 7, 8, 9 en 10.

---

## Installatie

Installeer het NuGet-pakket:

```bash
dotnet add package Tjidde.Logging
```

Of voeg het handmatig toe aan uw `.csproj`:

```xml
<PackageReference Include="Tjidde.Logging" Version="1.0.1" />
```

---

## Snel aan de slag

### ASP.NET Core / Generic Host

Verwijder in `Program.cs` de standaard providers en voeg de Tjidde-logger toe:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddTjiddeLogger(options =>
{
    options.IncludeScopes = true;
});

// Schakel eenvoudig tussen Text- en Json-formaten:
builder.Logging.UseTjiddeJsonFormat(); // Schakel naar JSON
// builder.Logging.UseTjiddeTextFormat(); // Schakel terug naar Text (standaard)

var app = builder.Build();
app.Run();
```

### Console / Worker Service

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddTjiddeLogger();
    })
    .Build();

await host.RunAsync();
```

### De logger injecteren en gebruiken

```csharp
public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger)
    {
        _logger = logger;
    }

    public void PlaceOrder(int orderId)
    {
        _logger.LogInformation("Bestelling {OrderId} succesvol geplaatst", orderId);
    }
}
```

---

## Loguitvoer formaat

Elke logmelding wordt als **één regel** geschreven in het volgende formaat:

```
YYYY-MM-DD: HH:mm:ss: [NIVEAU] CS=>KlasseNaam Method=>MethodeNaam: Client=>Klant: Bericht [Masked: sleutel1, sleutel2] | Exception: [...] | Scopes: scope1 > scope2
```

### Voorbeelduitvoer

```
2026-03-18: 09:15:41: [INFORMATION] CS=>OrderService Method=>PlaceOrder: Client=>AcmeCorp: Bestelling 42 succesvol geplaatst
2026-03-18: 09:15:42: [ERROR] CS=>PaymentService Method=>ProcessPayment: Client=>AcmeCorp: Betaling mislukt | Exception: [System.InvalidOperationException: Gateway timeout]
2026-03-18: 09:15:43: [WARNING] CS=>AuthService Method=>Login: Inloggen met password [REDACTED]
```

### Logniveaus

| Niveau | Label |
|---|---|
| Trace | `[TRACE]` |
| Debug | `[DEBUG]` |
| Information | `[INFORMATION]` |
| Warning | `[WARNING]` |
| Error | `[ERROR]` |
| Critical | `[CRITICAL]` |
| Metrics *(Information + event `Metrics`)* | `[METRICS]` |

### Consolekleuren

Elk logniveau wordt in een aparte consolekleur weergegeven voor snelle visuele herkenning:

| Niveau | Kleur |
|---|---|
| Trace | Grijs |
| Debug | Cyaan |
| Information | Groen |
| Warning | Geel |
| Error | Rood |
| Critical | Donkerrood |
| Metrics *(Information + event `Metrics`)* | Magenta |

---

## Configuratie-opties

Je kunt de Tjidde-logger in code configureren of rechtstreeks vanuit `appsettings.json`.

### Optie 1: `appsettings.json` (zonder options-lambda)

```json
{
  "TjiddeLogger": {
    "OutputFormat": "Json",
    "MinimumLevel": "Information",
    "CategoryMinimumLevels": {
      "Default": "Warning",
      "MyCompany.MyApp": "Information",
      "MyCompany.MyApp.Services.OrderService": "Debug"
    },
    "EnableOpenTelemetryExport": true,
    "OpenTelemetryActivitySourceName": "MyCompany.MyApp",
    "OpenTelemetryCreateFallbackActivity": false,
    "IncludeScopes": true,
    "IncludeStackTrace": true,
    "IncludeInnerExceptions": true,
    "EnableSensitiveDataMasking": true,
    "MaskPlaceholder": "[REDACTED]",
    "AdditionalSensitiveKeys": ["tenantKey", "internalCode"]
  }
}
```

```csharp
builder.Logging.AddTjiddeLogger(builder.Configuration);
// of bind een expliciete sectie:
builder.Logging.AddTjiddeLogger(builder.Configuration.GetSection("TjiddeLogger"));
```

### Dynamisch censureren (tijdens runtime)

Je kunt extra gevoelige woorden toevoegen om te censureren tijdens runtime vanuit je applicatie via `MaskedKeysContext`. Dit is handig voor het anonimiseren van gegevens die pas tijdens de uitvoering bekend zijn.

```csharp
using Tjidde.Logging.Masking;

// Redigeer een specifieke waarde globaal
MaskedKeysContext.Add("SuperGeheimToken", "PersoonsID");

// De logger zal deze woorden nu automatisch anonimiseren in alle toekomstige logberichten
_logger.LogInformation("Verwerken van token SuperGeheimToken voor gebruiker PersoonsID");
// Uitvoer: Verwerken van token [REDACTED] voor gebruiker [REDACTED]

// Je kunt ook sleutels verwijderen als ze niet langer gevoelig zijn
MaskedKeysContext.Remove("PersoonsID");
```

### Optie 2: code-gebaseerde opties

Geef een `Action<TjiddeLoggerOptions>`-delegate mee aan `AddTjiddeLogger` om het gedrag aan te passen:

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.IncludeScopes              = true;          // Scope-informatie opnemen in uitvoer
    options.UseUtcTimestamp            = false;         // true: tijdstempels in UTC in plaats van lokale tijd
    options.IncludeStackTrace          = true;          // Stack trace opnemen bij uitzonderingen
    options.IncludeInnerExceptions     = true;          // Inner exceptions opnemen
    options.EnableSensitiveDataMasking = true;          // Gevoelige waarden maskeren
    options.MaskPlaceholder            = "[REDACTED]";  // Vervangende tekst voor gemaskeerde waarden
    options.AdditionalSensitiveKeys    = ["tenantKey", "internalCode"]; // Extra te maskeren sleutels
});
```

### Alle opties

| Optie | Type | Standaard | Beschrijving |
|---|---|---|---|
| `OutputFormat` | `TjiddeLogOutputFormat` | `Text` | Weergavemodus: `Text` of `Json` (Elasticsearch-vriendelijke JSON-regel). |
| `MinimumLevel` | `LogLevel` | `Trace` | Globaal minimum logniveau als er geen categorie-override matcht. |
| `CategoryMinimumLevels` | `IDictionary<string, LogLevel>` | `{}` | Categorie-/namespace-/klasse-specifieke minimum niveaus. Ondersteunt `Default` en prefix-matching zoals `Logging:LogLevel`. |
| `EnableOpenTelemetryExport` | `bool` | `false` | Stuurt elke logregel als OpenTelemetry-event op de huidige `Activity` (voor OTEL-pipelines). |
| `OpenTelemetryActivitySourceName` | `string` | `Tjidde.Logging` | Activity source-naam voor optionele fallback-activity creatie. |
| `OpenTelemetryCreateFallbackActivity` | `bool` | `false` | Maakt een korte interne activity als er geen huidige `Activity` beschikbaar is. |
| `IncludeScopes` | `bool` | `true` | Voegt actieve scope-waarden toe aan de logregel. |
| `UseUtcTimestamp` | `bool` | `false` | Schrijft tijdstempels in UTC in plaats van lokale tijd. Zie [Tijdstempels en `TimeProvider`](#tijdstempels-en-timeprovider). |
| `ResolveMethodNameFromStackTrace` | `bool` | `false` | Haalt de methodenaam uit de stack trace als er geen `MethodName`-scope actief is. Doorloopt bij elke logregel de stack en is dus traag; gebruik liever `BeginMethodScope()`. |
| `IncludeStackTrace` | `bool` | `true` | Neemt de stack trace op bij het loggen van uitzonderingen. |
| `IncludeInnerExceptions` | `bool` | `true` | Neemt inner exceptions op in de opgemaakte uitvoer. |
| `EnableSensitiveDataMasking` | `bool` | `true` | Schakelt automatische maskering van gevoelige waarden in. |
| `MaskPlaceholder` | `string` | `[REDACTED]` | De tekst waarmee gemaskeerde waarden worden vervangen. |
| `AdditionalSensitiveKeys` | `IList<string>` | `[]` | Extra eigenschapsnamen die als gevoelig worden beschouwd. |

### JSON-uitvoermodus (`OutputFormat = Json`)

Wanneer `OutputFormat` op `Json` staat, wordt elke logmelding als één JSON-object (single-line) geschreven met stabiele velden voor ingestie in Elasticsearch en andere logplatformen.

Voorbeeldvorm:

```json
{
  "@timestamp": "2026-03-23T15:00:00.0000000+01:00",
  "message": "Order geplaatst",
  "level": "INFORMATION",
  "category": "MyCompany.MyApp.Services.OrderService",
  "class": "OrderService",
  "eventId": 42,
  "eventName": "OrderCreated",
  "customer": "AcmeCorp",
  "method": "CreateOrder",
  "scopes": ["RequestId=123"],
  "maskedFields": ["password"],
  "exception": null,
  "properties": {
    "OrderId": 1001,
    "Amount": 29.95
  }
}
```

### Tijdstempels en `TimeProvider`

Tijdstempels zijn standaard lokale tijd. Zet `UseUtcTimestamp` aan om UTC te schrijven; meestal handig als logs van servers in verschillende tijdzones op één plek samenkomen:

```json
{
  "TjiddeLogger": {
    "UseUtcTimestamp": true
  }
}
```

- Tekstuitvoer houdt het formaat `yyyy-MM-dd: HH:mm:ss` zonder offset; let bij het lezen dus op `UseUtcTimestamp`.
- JSON-uitvoer schrijft `@timestamp` als ISO-8601 met offset: `2026-10-02T10:15:00.0000000+00:00` met `UseUtcTimestamp`, zonder die optie de lokale offset (bijvoorbeeld `+02:00`).

De klok is een `System.TimeProvider`. Is er een geregistreerd in DI, dan gebruikt Tjidde.Logging die; anders `TimeProvider.System`. In tests registreer je een `FakeTimeProvider` (package `Microsoft.Extensions.TimeProvider.Testing`) voor voorspelbare tijdstempels:

```csharp
var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 10, 15, 0, TimeSpan.Zero));
services.AddSingleton<TimeProvider>(clock);
services.AddLogging(logging => logging.AddTjiddeLogger(options => options.UseUtcTimestamp = true));
```

Zonder DI geef je hem mee aan de constructor: `new TjiddeLoggerProvider(optionsMonitor, customerContextAccessor, maskedKeysAccessor, clock)`.

### OpenTelemetry-integratie

Schakel OpenTelemetry-export in om elke Tjidde-logregel als event toe te voegen aan de actieve trace-`Activity`.

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.EnableOpenTelemetryExport = true;
    options.OpenTelemetryActivitySourceName = "MyCompany.MyApp";
    options.OpenTelemetryCreateFallbackActivity = false;
});
```

Event-tags bevatten onder andere `log.level`, `log.message`, `log.category`, `event.id`, `event.name`, `code.function` en exception-details.

---

## Klantcontext

De klantcontext maakt het mogelijk om elke logmelding te voorzien van een klant- of tenant-identificatie, zonder deze door elke methodeaanroep te hoeven doorgeven. Het maakt gebruik van `AsyncLocal<T>`, zodat het correct doorstroomt via `async`/`await`-ketens.

### Context instellen

```csharp
// Aan het begin van een verzoek, middleware of achtergrondtaak:
CustomerContext.Set("AcmeCorp");

// Alle logmeldingen vanaf dit punt bevatten "AcmeCorp:" in de uitvoer.
_logger.LogInformation("Verwerking gestart");
// → 2026-03-18: 09:00:00: [INFORMATION] MyService: AcmeCorp: Verwerking gestart
```

### Context wissen

```csharp
CustomerContext.Clear();
```

### Huidige waarde opvragen

```csharp
var current = CustomerContext.Current; // geeft null terug als er niets is ingesteld
```

### Typisch gebruik in ASP.NET Core middleware

```csharp
public class CustomerContextMiddleware
{
    private readonly RequestDelegate _next;

    public CustomerContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var customerId = context.User?.FindFirst("customer_id")?.Value;
        CustomerContext.Set(customerId);
        try
        {
            await _next(context);
        }
        finally
        {
            CustomerContext.Clear();
        }
    }
}
```

---

## Maskering van gevoelige gegevens

Maskering is standaard ingeschakeld. De masker scant zowel de opgemaakte berichtstring als gestructureerde logeigenschappen.

### Ingebouwde gevoelige sleutels

De volgende sleutels worden automatisch gemaskeerd (hoofdletterongevoelig, `-` en `_` worden genegeerd):

`password`, `wachtwoord`, `token`, `accesstoken`, `access_token`, `refreshtoken`, `refresh_token`, `secret`, `clientsecret`, `client_secret`, `apikey`, `api_key`, `x-api-key`, `authorization`, `bearer`, `cookie`, `set-cookie`

### Ondersteunde berichtpatronen

| Patroon | Voorbeeldinvoer | Resultaat |
|---|---|---|
| `sleutel=waarde` | `password=hunter2` | `password=[REDACTED]` |
| `sleutel: waarde` | `token: abc123` | `token: [REDACTED]` |
| `"sleutel": "waarde"` (JSON; waarden tussen aanhalingstekens worden tot het sluitende aanhalingsteken gemaskeerd) | `"password": "my secret"` | `"password": "[REDACTED]"` |
| HTTP-authenticatieschema (het schema blijft zichtbaar) | `Authorization: Bearer abc.def` | `Authorization: Bearer [REDACTED]` |
| `sleutel waarde` (waarde van 4 of meer tekens) | `password hunter2` | `password [REDACTED]` |

> **Let op:** korte woorden na een sleutel, zoals in `"password is missing"`, worden **niet** gemaskeerd. Langere woorden wel: `Refreshing token cache` wordt `Refreshing token [REDACTED]`.
>
> Duurt het maskeren van een heel groot bericht te lang, dan wordt het hele bericht vervangen door de plaatshouder in plaats van ongemaskeerd weggeschreven.

### Aangepaste gevoelige sleutels toevoegen

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.AdditionalSensitiveKeys = ["tenantKey", "internalCode", "bsn"];
});
```

### Aangepaste plaatshouder

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.MaskPlaceholder = "***";
});
```

### Maskering uitschakelen

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.EnableSensitiveDataMasking = false;
});
```

---

## Uitzonderingsopmaak

Uitzonderingen worden inline op dezelfde logregel opgemaakt in een compact, leesbaar formaat:

```
| Exception: [System.InvalidOperationException: Er is iets misgegaan | StackTrace: at MyApp.Service.DoWork() ...] -> [System.ArgumentNullException: Waarde mag niet null zijn]
```

- Elke uitzondering wordt omsloten door `[UitzonderingsType: Bericht | StackTrace: ...]`.
- Inner exceptions worden toegevoegd met ` -> [...]`.
- Stack trace-frames worden gescheiden door ` | `.

### Uitzonderingsuitvoer beheren

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.IncludeStackTrace      = false; // stack trace weglaten
    options.IncludeInnerExceptions = false; // inner exceptions weglaten
});
```

---

## Scopes

Scopes maken het mogelijk om contextuele sleutel-waardeparen of strings te koppelen aan een groep logmeldingen.

### Scopes gebruiken

```csharp
using (_logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = "req-001" }))
{
    _logger.LogInformation("Verzoek wordt verwerkt");
    // → ... | Scopes: RequestId=req-001
}
```

### Methodenaam-scope

De logger haalt automatisch een `MethodName`-sleutel op uit de actieve scope en plaatst deze direct na de klassenaam in de logregel (niet in de Scopes-sectie). `BeginMethodScope()` vult de naam van de aanroepende methode al tijdens het compileren in en kost dus niets tijdens het draaien:

```csharp
public void PlaceOrder()
{
    using (_logger.BeginMethodScope())
    {
        _logger.LogInformation("Bestelling geplaatst");
        // → 2026-03-18: 09:00:00: [INFORMATION] Class=>OrderService Method=>PlaceOrder: Bestelling geplaatst
    }
}
```

Dit is hetzelfde als `_logger.BeginScope(new Dictionary<string, object> { ["MethodName"] = "PlaceOrder" })`.

Zonder `MethodName`-scope wordt de methodenaam weggelaten. Zet `ResolveMethodNameFromStackTrace = true` om hem uit de stack trace te halen; dat doorloopt bij elke logregel de stack, dus vermijd het in veelgebruikte code.

### Scopes uitschakelen

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.IncludeScopes = false;
});
```

---

## Metrics-regels

Tjidde.Logging biedt `LogMetrics` voor het schrijven van metric-achtige regels (tellers, tijdmetingen, meters) naast reguliere logberichten. Een metrics-regel is een gewone `LogLevel.Information`-regel met het event-ID `MetricsLoggerExtensions.MetricsEventId` (`Id = 10000`, `Name = "Metrics"`). Tjidde.Logging herkent dat event en toont de regel als `[METRICS]`; andere providers (bijvoorbeeld `AddConsole()`) zien een gewone information-regel.

> Gebruik voor echte applicatiemetrics (dashboards, alerting, aggregatie) [`System.Diagnostics.Metrics`](https://learn.microsoft.com/dotnet/core/diagnostics/metrics) met OpenTelemetry of `dotnet-counters`. `LogMetrics` is bedoeld voor metricwaarden die u ook in de log wilt zien.

### LogMetrics gebruiken

Voeg de using-directive toe en roep `LogMetrics` aan:

```csharp
using Tjidde.Logging.Extensions;

// Eenvoudig bericht
_logger.LogMetrics("requests_total=42");

// Met opmaakargumenten
_logger.LogMetrics("response_time_ms={ResponseTime}", elapsed.TotalMilliseconds);

// Met een uitzondering
_logger.LogMetrics(ex, "payment_failures_total={Count}", failureCount);

// Met een event-ID: een event-ID zonder naam krijgt de naam "Metrics" en wordt als [METRICS] getoond;
// een event-ID met een andere naam wordt ongewijzigd doorgegeven en als [INFORMATION] getoond.
_logger.LogMetrics(new EventId(200), "throughput_rps={Rps}", rps);
```

### Voorbeelduitvoer

```
2026-03-18: 11:00:00: [METRICS] OrderService ProcessOrder: AcmeCorp: requests_total=42
```

Het label `[METRICS]` wordt in **Magenta** weergegeven in de console voor eenvoudige visuele herkenning.

### Filteren

Het eigen `MinimumLevel` en `CategoryMinimumLevels` van Tjidde.Logging laten metrics-regels altijd door. Filters van `Microsoft.Extensions.Logging` zelf (bijvoorbeeld `Logging:LogLevel:Default` in `appsettings.json`) behandelen ze als `Information`; een categorie die op `Warning` of hoger staat, filtert dus ook haar metrics-regels.

### Het verouderde `Metrics`-logniveau

Eerdere versies logden metrics op het aangepaste niveau `MetricsLoggerExtensions.Metrics` (`(LogLevel)10`). Die waarde is geen geldig `LogLevel`: andere providers weigeren haar en de console-formatters van Microsoft gooien een `ArgumentOutOfRangeException`, waardoor `LogMetrics` crashte zodra ook `AddConsole()` geregistreerd was. Het veld is nu gemarkeerd als `[Obsolete]`. Tjidde.Logging toont `(LogLevel)10` nog steeds als `[METRICS]`, maar gebruik in plaats daarvan `LogMetrics(...)` (of `MetricsEventId`).

---

## CI/CD (GitHub Actions)

In `.github/workflows/` staan twee workflows:

| Workflow | Draait bij | Wat het doet |
|---|---|---|
| `ci.yml` | Elke push naar `main` en elke pull request | Bouwt de solution, draait de tests, maakt het NuGet-pakket en bewaart het als build-artefact. |
| `publish.yml` | Het pushen van een versietag zoals `v1.2.3` | Bouwt en test met de versie uit de tag en publiceert daarna het pakket en het symbolenpakket (`.snupkg`) op nuget.org. |

Publiceren gebeurt met [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing). GitHub bewijst aan nuget.org welke repository en workflow publiceert, en krijgt een API-sleutel terug die één uur geldig is. Er wordt nergens een vaste API-sleutel opgeslagen.

### Eenmalige instelling

1. Open op nuget.org je accountmenu, kies **Trusted Publishing** en voeg een policy toe:
   - **Repository Owner:** `tjidde-nl`
   - **Repository:** `TjiddeLogger`
   - **Workflow File:** `publish.yml`
   - **Environment:** leeg laten

   Vraagt het formulier om scopes, sta dan zowel nieuwe pakketten als nieuwe versies toe (bijvoorbeeld met het patroon `Tjidde.*`): de eerste release maakt het pakket `Tjidde.Logging` aan.
2. Ga in de GitHub-repository naar **Settings → Secrets and variables → Actions** en voeg een repository-secret `NUGET_USER` toe met je gebruikersnaam op nuget.org (je profielnaam, niet je e-mailadres).

Bij een privérepository is de policy eerst maar 7 dagen actief. Na de eerste geslaagde publicatie wordt hij permanent.

### Een versie uitbrengen

Voeg eerst een sectie `## [1.0.4] - <datum>` toe aan `CHANGELOG.md` en commit die. De changelog wordt de release notes van het pakket op nuget.org, en de publish-workflow stopt als de getagde versie geen sectie heeft. Tag en push daarna:

```bash
git tag v1.0.4
git push origin v1.0.4
```

De tag bepaalt de pakketversie: `v1.0.4` publiceert `1.0.4`, en pre-releases zoals `v1.1.0-beta.1` werken ook. De `<Version>` in de `.csproj` geldt alleen voor lokale builds. Een versie die al op nuget.org staat, wordt overgeslagen in plaats van dat de workflow faalt.

---

## Toekomstige ontwikkelingen

De volgende verbeteringen en functies zijn gepland of worden overwogen voor toekomstige releases:

- **Bestandssink** — Loguitvoer wegschrijven naar roterende logbestanden naast de console.
- **Redactie-auditlog** — Optioneel een aparte auditmelding uitsturen met de lijst van gemaskeerde velden, voor compliancescenario's.
- **Aangepaste gevoelige-sleutelproviders** — Het injecteren van `ISensitiveKeyProvider`-implementaties toestaan, zodat sleutels tijdens runtime uit configuratie of een secrets store kunnen worden geladen.
- **NuGet-pakketondertekening** — Het NuGet-pakket ondertekenen in de pipeline voor supply-chain-beveiliging.
- **Blazor / MAUI-voorbeeld** — De demo-app uitbreiden om realtime logstreaming te tonen in een Blazor-UI of MAUI-desktopapp.
