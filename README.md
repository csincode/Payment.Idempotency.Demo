# 🧠 Implementando Idempotência em Minimal APIs com ASP.NET Core

Este exemplo demonstra como implementar **idempotência em uma API Minimal** usando **.NET 8+**, garantindo que múltiplas requisições idênticas (com o mesmo `Idempotency-Key`) resultem em **uma única execução lógica** — mesmo em cenários de falhas, retries ou concorrência.

---

## 🚀 Objetivo

Evitar efeitos colaterais **duplicados** em operações sensíveis (como pagamentos, criação de pedidos, ou transferências) quando o cliente tenta reenviar a mesma requisição por falha de rede ou timeout.

Em outras palavras:

> Se a mesma requisição chegar duas vezes com o mesmo `Idempotency-Key`, o servidor deve retornar **a mesma resposta**, sem reprocessar a lógica.

---

## 📁 Estrutura do Código

O projeto é composto por um único arquivo `Program.cs`, contendo:

1. Um endpoint `/payment` (Minimal API).
2. Um **filtro de endpoint customizado** (`IdempotencyFilter`) responsável pela lógica de idempotência.
3. Um **cache em memória (`IMemoryCache`)** para armazenar as respostas.
4. Controle de **concorrência** com bloqueio (lock TTL curto).
5. Hash do corpo da requisição para validar consistência do payload.

---

## 🧩 Funcionamento Detalhado

### 1️⃣ O Header de Idempotência

Toda requisição deve conter o cabeçalho:

```http
Idempotency-Key: <guid ou string única>
```

Esse valor é usado como **chave de identificação única da operação**.  
Se o mesmo `Idempotency-Key` for usado novamente, a API entenderá que é uma **repetição da mesma intenção**.

---

### 2️⃣ O Filtro `IdempotencyFilter`

Esse filtro implementa a interface `IEndpointFilter`, que permite interceptar chamadas antes e depois do endpoint ser executado.

Ele executa a seguinte sequência:

#### 🧩 Passo 1 – Validação do cabeçalho

```csharp
if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var keyHeader)
    || string.IsNullOrWhiteSpace(keyHeader))
{
    return Results.Json(new { error = "Idempotency-Key header is required" }, statusCode: 400);
}
```

---

#### 🧩 Passo 2 – Geração do Hash do Corpo

```csharp
var body = context.Arguments.FirstOrDefault();
var bodyHash = ComputeHash(body);
```

---

#### 🧩 Passo 3 – Busca de Resposta em Cache

```csharp
if (cache.TryGetValue(cacheKey, out CachedResponse cached))
{
    if (cached.BodyHash != bodyHash)
        return Results.Json(new { error = "Request body does not match..." }, statusCode: 400);

    return Results.Json(cached.Body, statusCode: cached.StatusCode);
}
```

---

#### 🧩 Passo 4 – Lock de Execução Concorrente

```csharp
if (cache.TryGetValue(lockKey, out _))
    return Results.Json(new { error = "Request is already in progress" }, statusCode: 409);
```

---

#### 🧩 Passo 5 – Execução da Operação

```csharp
var result = await next(context);
```

---

#### 🧩 Passo 6 – Cache da Resposta

```csharp
if (result is IStatusCodeHttpResult statusCodeResult && result is IValueHttpResult valueResult)
{
    int statusCode = statusCodeResult.StatusCode ?? 200;

    var cachedResponse = new CachedResponse
    {
        StatusCode = statusCode,
        Body = valueResult.Value,
        BodyHash = bodyHash
    };

    cache.Set(cacheKey, cachedResponse, TimeSpan.FromMinutes(cacheTimeInMinutes));
}
```

---

### 3️⃣ O Endpoint `/payment`

```csharp
app.MapPost("/payment", PaymentHandler)
   .AddEndpointFilter<IdempotencyFilter>();
```

---

## 🧠 Por que isso é importante?

Imagine um cliente chamando o endpoint `/payment` duas vezes por causa de timeout.  
Sem idempotência, você teria **dois pagamentos duplicados**.  
Com o filtro, ambas as chamadas retornam **a mesma resposta original**.

---

## ⚙️ Configurações e Extensões

| Recurso | Implementado | Pode ser expandido com |
|----------|---------------|-------------------------|
| Cache local | ✅ IMemoryCache | Redis (`IDistributedCache`) |
| TTL da resposta | 15 min | Configurável |
| Lock de concorrência | ✅ 2 min | Redis Lock / Semaphore |
| Verificação do body | ✅ SHA256 | HMAC para maior segurança |
| Status codes retornados | 200 / 400 / 409 | Personalizável |

---

## 🧾 Exemplo de Uso (cURL)

### Primeira chamada

```bash
curl -X POST https://localhost:5001/payment \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: 123e4567-e89b-12d3-a456-426614174000" \
  -d "{ \"amount\": 150.00 }"
```

🟢 **Resposta:**

```json
{
  "message": "Payment processed successfully",
  "order": "a8f2b1d3",
  "amount": 150.00,
  "date": "2025-11-12T22:00:00Z"
}
```

---

### Segunda chamada com o mesmo `Idempotency-Key`

```bash
curl -X POST https://localhost:5001/payment \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: 123e4567-e89b-12d3-a456-426614174000" \
  -d "{ \"amount\": 150.00 }"
```

🟢 **Resposta:**

```json
{
  "message": "Payment processed successfully",
  "order": "a8f2b1d3",
  "amount": 150.00,
  "date": "2025-11-12T22:00:00Z"
}
```

---

## 🧱 Conclusão

Essa implementação demonstra uma arquitetura **idempotente e thread-safe** para Minimal APIs no .NET 8:

- ✅ Usa `IEndpointFilter` para integração nativa.  
- ✅ Evita duplicações de transações.  
- ✅ É extensível para Redis, Kafka, RabbitMQ, etc.  
- ✅ Fornece segurança e previsibilidade no consumo da API.  
