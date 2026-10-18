# SZA.AI CLI (`sza`)

CLI para ativar e gerenciar o [SZA.AI Proxy](../SZA.AI.Proxy/README.md), no estilo `docker`: `sza <grupo> <comando> [opções]`.

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (para compilar o proxy e instalar como dotnet tool)

## Instalação

### Como dotnet tool

```powershell
.\publish.ps1 -Version 1.0.0 -Install
```

Ou manualmente:

```powershell
dotnet pack SZA.AI.CLI -c Release -o .\nupkg
dotnet tool install -g --add-source .\nupkg SZA.AI.CLI
```

### Com `sza install`

```powershell
dotnet publish SZA.AI.CLI -c Release -o .\publish
.\publish\sza.exe install
```

Copia os arquivos para `%LOCALAPPDATA%\sza\bin` e adiciona a pasta ao PATH do usuário. Abra um novo terminal depois. Use `--path <dir>` para escolher outra pasta e `sza uninstall` para remover.

> Use apenas uma das formas de instalação para evitar dois `sza` no PATH.

### Sem instalar

```powershell
dotnet run --project SZA.AI.CLI -- --help
```

## Comandos

| Comando | Descrição |
| --- | --- |
| `sza proxy start` | Inicia o proxy (Ctrl+C encerra) |
| `sza proxy stop` | Encerra o proxy iniciado pelo CLI |
| `sza proxy status` | Estado, URL, PID, versão e contagens |
| `sza proxy logs` | Mostra os logs do proxy |
| `sza proxy open` | Abre a documentação Scalar no navegador |
| `sza provider ls` | Lista os providers |
| `sza provider add <id>` | Cadastra um provider (exige `--url`) |
| `sza provider edit <id>` | Altera um provider |
| `sza provider rm <id>` | Remove o provider e sua chave |
| `sza provider set-key <id>` | Define a API key |
| `sza provider rm-key <id>` | Remove a API key |
| `sza model ls` | Lista os modelos |
| `sza model add <provider> <local>` | Cadastra um modelo |
| `sza model edit <provider> <local>` | Altera um modelo |
| `sza model rm <provider> <local>` | Remove um modelo |
| `sza model import <provider>` | Importa os modelos expostos pelo provider |
| `sza config show\|set\|reset` | Mostra, altera ou restaura a configuração do CLI |
| `sza install` / `sza uninstall` | Instala ou remove o `sza` do PATH |
| `sza version` | Mostra a versão |

Use `sza <grupo> --help` para ver as opções de cada grupo.

### Opções principais

| Opção | Usada em | Descrição |
| --- | --- | --- |
| `--port <n>` | `proxy start`, `config set` | Porta do proxy (0 = porta livre) |
| `-d`, `--detach` | `proxy start` | Inicia em segundo plano |
| `--no-build` | `proxy start` | Não compila o proxy antes de iniciar |
| `--lines <n>`, `-f` | `proxy logs` | Linhas exibidas / acompanha novos logs |
| `--proxy <caminho>` | `config set` | `.csproj`, `.exe` ou pasta do SZA.AI.Proxy |
| `--url`, `--name`, `--api-key` | `provider add/edit` | Dados do provider |
| `--auth-header`, `--auth-scheme` | `provider add/edit` | Autenticação (padrão: `Authorization` / `Bearer`) |
| `--route <rota=destino>` | `provider add/edit` | Mapeia uma rota do provider (repetível) |
| `--context-length`, `--max-output`, `--temperature` | `provider`/`model add/edit` | Limites e temperatura (0 a 2) |
| `--vision-model`, `--vision-prompt` | `provider add/edit` | Descrição de imagens |
| `--remote <nome>` | `model add/edit` | Nome do modelo no provider |
| `--vision` / `--no-vision` | `model add/edit` | Suporte a visão |
| `--active` / `--inactive` | `provider`/`model add/edit` | Ativa ou desativa |
| `--verbose` | todos | Mostra detalhes de erros inesperados |

## Primeiros passos

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

`sza provider set-key` pede a chave sem exibi-la. Passar `--api-key` na linha de comando deixa a chave no histórico do shell.

## Configuração e dados

- A configuração do CLI (porta e caminho do proxy) é gerenciada por `sza config`.
- Os logs do proxy iniciado pelo CLI podem ser vistos com `sza proxy logs`.
- Os comandos `provider` e `model` exigem o proxy em execução.

## Publicação

```powershell
.\publish.ps1 -Version 1.2.0                       # gera o pacote em .\nupkg
.\publish.ps1 -Version 1.2.0 -Install             # gera e instala como ferramenta global
.\publish.ps1 -Version 1.2.0 -Source <feed> -ApiKey <chave>   # gera e envia ao feed
```

Antes de publicar, registre as mudanças no [CHANGELOG](CHANGELOG.md).
