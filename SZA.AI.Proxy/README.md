# SZA.AI Proxy

Proxy HTTP (ASP.NET Core, .NET 10) que expõe uma API compatível com **Ollama** e **OpenAI** e encaminha as chamadas para os providers cadastrados (qualquer serviço com API compatível). Providers, modelos e chaves são gerenciados em tempo de execução, sem reiniciar o serviço.

Para gerenciar o proxy pela linha de comando, use o [CLI `sza`](../SZA.AI.CLI/README.md).

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Executando

```powershell
dotnet run --project SZA.AI.Proxy
```

Ou pelo CLI:

```powershell
sza proxy start --detach
```

Em ambiente `Development`, a documentação interativa (Scalar) fica disponível; com o CLI, use `sza proxy open`.

### Porta

Ordem de prioridade:

1. Argumento `--port`
2. Variável de ambiente `SZA_AI_PORT`
3. Configuração `Proxy:Port`
4. Padrão: **11434** (a mesma do Ollama)

Porta `0` escolhe uma porta livre. Os endereços em uso são gravados em `Data/runtime.json`, que o CLI lê para descobrir a URL.

### Resiliência

O serviço permanece de pé: exceções não tratadas são registradas e o host é recriado após 3 segundos. Erros em requisições devolvem `500` sem derrubar o processo.

### Streams longos e keep-alive

Gerações longas não têm timeout total: o `HttpClient` do provider usa `Timeout.InfiniteTimeSpan` e o Kestrel está configurado sem limite de taxa de resposta, com `KeepAliveTimeout` de 1 hora.

Enquanto o provider fica em silêncio (por exemplo, um modelo de raciocínio que demora a emitir o primeiro token), o proxy envia comentários SSE (`: keep-alive`) no stream de `/v1` para que os clientes não derrubem a conexão por ociosidade — era o que fazia o Copilot CLI ficar “aguardando resposta” quando o provider demorava.

| Configuração | Padrão | Descrição |
| --- | --- | --- |
| `Proxy:StreamHeartbeatSeconds` | `15` | Intervalo do keep-alive SSE durante o silêncio do provider. `0` desativa. |

Também pode ser definido pela variável de ambiente `Proxy__StreamHeartbeatSeconds` (ou `SZA_AI_PROXY_STREAMHEARTBEATSECONDS`).

## Endpoints

| Grupo | Rota base | Descrição |
| --- | --- | --- |
| Ollama | `/api` | Endpoints compatíveis com a API do Ollama |
| OpenAI | `/v1` | Endpoints compatíveis com a API da OpenAI |
| Providers | `/api/providers`, `/v1/providers` | Cadastro de providers e modelos |

### Gerenciamento de providers e modelos

| Método | Rota | Descrição |
| --- | --- | --- |
| `GET` | `/api/providers` | Lista os providers |
| `GET` | `/api/providers/{id}` | Detalha um provider |
| `POST` / `PUT` | `/api/providers` | Cria ou atualiza (upsert) um provider |
| `DELETE` | `/api/providers/{id}` | Remove o provider e sua chave |
| `PUT` | `/api/providers/{id}/key` | Define a API key (`{ "apiKey": "..." }`) |
| `DELETE` | `/api/providers/{id}/key` | Remove a API key |
| `GET` | `/api/providers/{id}/models` | Lista os modelos do provider |
| `POST` / `PUT` | `/api/providers/{id}/models` | Cria ou atualiza (upsert) um modelo |
| `DELETE` | `/api/providers/{id}/models/{localName}` | Remove um modelo |
| `POST` | `/api/providers/{id}/models/import` | Importa os modelos expostos pelo provider (exige a rota `/api/tags` mapeada) |

A API key é **somente escrita**: nenhum endpoint a devolve. As respostas trazem apenas `hasApiKey`.

## Modelo de dados

### Provider

| Campo | Descrição |
| --- | --- |
| `id` | Identificador (obrigatório) |
| `name` | Nome de exibição |
| `baseUrl` | URL base do provider (obrigatório) |
| `active` | Ativo (padrão: `true`) |
| `apiKeyRef` | Referência da chave no cofre; se vazio, usa o `id` |
| `authHeader` / `authScheme` | Autenticação (padrão: `Authorization` / `Bearer`) |
| `contextLength`, `maxOutputTokens`, `temperature` | Valores padrão do provider |
| `visionModel`, `visionPrompt` | Modelo e prompt usados para descrever imagens |
| `routes` | Mapa de rotas do proxy para rotas do provider (ex.: `/api/tags` → `/v1/models`) |

### Modelo

| Campo | Descrição |
| --- | --- |
| `providerId` | Provider dono do modelo |
| `localName` | Nome exposto pelo proxy (obrigatório) |
| `remoteName` | Nome no provider (padrão: `localName`) |
| `active` | Ativo (padrão: `true`) |
| `contextLength`, `maxOutputTokens`, `temperature` | Sobrescrevem os valores do provider |
| `supportsVision` | Suporta imagens |

## Dados e segredos

Os arquivos ficam na pasta `Data` ao lado do executável:

| Arquivo | Conteúdo |
| --- | --- |
| `providers.json` | Providers cadastrados |
| `models.json` | Modelos cadastrados |
| `secrets.json` | API keys, protegidas com ASP.NET Core Data Protection |
| `runtime.json` | Endereços em uso e horário de início |

> Não versione `secrets.json`. As chaves só podem ser lidas pela mesma máquina/usuário que as gravou.

## Logs

Os logs usam Serilog (console e arquivo). Para acompanhá-los pelo CLI: `sza proxy logs --follow`.

## Exemplo

```powershell
# Cadastra um provider e uma chave via API
curl -X POST http://localhost:11434/api/providers `
  -H "Content-Type: application/json" `
  -d '{ "id": "openai", "name": "OpenAI", "baseUrl": "https://api.openai.com", "routes": { "/api/tags": "/v1/models" } }'

curl -X PUT http://localhost:11434/api/providers/openai/key `
  -H "Content-Type: application/json" `
  -d '{ "apiKey": "sk-..." }'

# Importa os modelos do provider
curl -X POST http://localhost:11434/api/providers/openai/models/import
```
