# SZA.AI

[![NuGet](https://img.shields.io/nuget/v/SZA.AI.CLI?logo=nuget)](https://www.nuget.org/packages/SZA.AI.CLI)
[![Downloads](https://img.shields.io/nuget/dt/SZA.AI.CLI)](https://www.nuget.org/packages/SZA.AI.CLI)
[![Licença](https://img.shields.io/github/license/szarblewski/SZA.AI)](https://github.com/szarblewski/SZA.AI/blob/master/LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/download)

Proxy HTTP local que expõe uma API compatível com **Ollama** e **OpenAI** e encaminha as chamadas para os providers cadastrados — qualquer serviço com API compatível. Providers, modelos e chaves são gerenciados em tempo de execução, sem reiniciar o serviço.

O repositório traz também o **`sza`**, um CLI no estilo `docker` para ativar e gerenciar o proxy.

## Componentes

| Projeto | Descrição | Documentação |
| --- | --- | --- |
| [`SZA.AI.Proxy`](https://github.com/szarblewski/SZA.AI/tree/master/SZA.AI.Proxy) | Proxy ASP.NET Core (.NET 10) com endpoints Ollama e OpenAI | [README do proxy](https://github.com/szarblewski/SZA.AI/blob/master/SZA.AI.Proxy/README.md) |
| [`SZA.AI.CLI`](https://github.com/szarblewski/SZA.AI/tree/master/SZA.AI.CLI) | CLI `sza`, distribuído como dotnet tool | [README do CLI](https://github.com/szarblewski/SZA.AI/blob/master/SZA.AI.CLI/README.md) |

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Instalação

Instale o CLI como ferramenta global:

```powershell
dotnet tool install --global SZA.AI.CLI
```

Atualize depois com:

```powershell
dotnet tool update --global SZA.AI.CLI
```

> Também é possível executar `sza` sem instalar, com `dotnet run --project SZA.AI.CLI -- --help`, ou copiar os binários para o PATH com `sza install`.

## Início rápido

```powershell
# 1. Aponte o CLI para o proxy (só uma vez, se não estiver rodando de dentro do repositório)
sza config set --proxy C:\caminho\SZA.AI.Proxy\SZA.AI.Proxy.csproj

# 2. Inicie o proxy em segundo plano
sza proxy start --detach

# 3. Cadastre um provider, a chave e os modelos
sza provider add openai --url https://api.openai.com --name OpenAI --route /api/tags=/v1/models
sza provider set-key openai
sza model import openai

# 4. Verifique
sza proxy status
sza provider ls
sza model ls
```

Por padrão o proxy escuta em `http://localhost:11434` (a mesma porta do Ollama). Use `--port` ou a variável `SZA_AI_PORT` para alterar.

## Comandos principais

| Comando | Descrição |
| --- | --- |
| `sza proxy start\|stop\|status\|logs\|open` | Inicia, encerra, inspeciona, acompanha os logs e abre a documentação |
| `sza proxy autostart on\|off\|status` | Inicia o proxy junto com o Windows |
| `sza provider ls\|add\|edit\|rm` | Gerencia providers |
| `sza provider set-key\|rm-key` | Define ou remove a API key de um provider |
| `sza model ls\|add\|edit\|rm\|import` | Gerencia modelos |
| `sza config show\|set\|reset` | Mostra, altera ou restaura a configuração do CLI |
| `sza install` / `sza uninstall` | Instala ou remove o `sza` do PATH |
| `sza version` | Mostra a versão |

Use `sza <grupo> --help` para ver as opções de cada grupo. A referência completa está no [README do CLI](https://github.com/szarblewski/SZA.AI/blob/master/SZA.AI.CLI/README.md).

## Endpoints

| Grupo | Rota base | Descrição |
| --- | --- | --- |
| Ollama | `/api` | Compatível com a API do Ollama |
| OpenAI | `/v1` | Compatível com a API da OpenAI |
| Providers | `/api/providers`, `/v1/providers` | Cadastro de providers e modelos |

Os detalhes de cada endpoint, o modelo de dados e os arquivos de configuração estão no [README do proxy](https://github.com/szarblewski/SZA.AI/blob/master/SZA.AI.Proxy/README.md).

## Estrutura do repositório

```text
SZA.AI/
├── SZA.AI.CLI/             # CLI `sza` (dotnet tool)
│   ├── CHANGELOG.md
│   └── README.md
├── SZA.AI.Proxy/           # Proxy ASP.NET Core
│   ├── Data/               # providers, modelos e templates
│   └── README.md
├── publish.ps1             # Gera, instala e publica o pacote NuGet
├── SZA.AI.slnx
└── LICENSE.txt
```

## Desenvolvimento

```powershell
# Compilar a solução
dotnet build SZA.AI.slnx

# Executar o proxy
dotnet run --project SZA.AI.Proxy

# Executar o CLI sem instalar
dotnet run --project SZA.AI.CLI -- --help
```

## Publicação do pacote

A versão do pacote fica em `SZA.AI.CLI/SZA.AI.CLI.csproj`:

```powershell
.\publish.ps1 -Version 1.2.0                                   # gera o pacote em .\nupkg
.\publish.ps1 -Version 1.2.0 -Install                          # gera e instala como ferramenta global
.\publish.ps1 -Version 1.2.0 -Source <feed> -ApiKey <chave>    # gera e envia ao feed
```

Antes de publicar, registre as mudanças no [CHANGELOG](https://github.com/szarblewski/SZA.AI/blob/master/SZA.AI.CLI/CHANGELOG.md).

## Licença

Distribuído sob a licença disponível em [LICENSE.txt](https://github.com/szarblewski/SZA.AI/blob/master/LICENSE.txt).
