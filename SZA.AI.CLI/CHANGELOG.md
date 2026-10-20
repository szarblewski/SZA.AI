# Changelog

Todas as mudanças relevantes do CLI `sza` são registradas aqui.

O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto usa [Versionamento Semântico](https://semver.org/lang/pt-BR/).

Tipos de mudança: **Adicionado**, **Alterado**, **Corrigido**, **Removido**, **Segurança**.

## [Não lançado]

## [1.0.8] - 2026-10-20

## [1.0.7] - 2026-10-19

## [1.0.6] - 2026-10-19

## [1.0.5] - 2026-10-18

## [1.0.4] - 2026-10-18

## [1.0.3] - 2026-10-18

## [1.0.2] - 2026-10-18

## [1.0.1] - 2026-10-18

## [2.0.1] - 2026-10-18

## [2.0.0] - 2026-10-18

## [1.1.0] - 2026-10-18

### Adicionado

- Comandos `sza provider add`, `edit`, `rm`, `set-key` e `rm-key` para cadastrar, editar e remover providers e suas API keys.
- Comandos `sza model add`, `edit`, `rm` e `import` para gerenciar modelos.
- Script `publish.ps1` para gerar, instalar e enviar o pacote com a versão informada.
- READMEs do CLI e do proxy.

## [1.0.0] - 2026-10-18

### Adicionado

- CLI no estilo Docker: `sza <grupo> <comando>`.
- Grupos `proxy` (`start`, `stop`, `status`, `logs`, `open`), `provider ls`, `model ls` e `config` (`show`, `set`, `reset`).
- `sza install` e `sza uninstall` para colocar o `sza` no PATH do usuário.
- Distribuição como dotnet tool (`SZA.AI.CLI`).

### Removido

- Modo interativo (Terminal.Gui) e os comandos planos (`sza start`, `sza status` etc.), substituídos pelos comandos agrupados.

[Não lançado]: https://github.com/szarblewski/SZA.AI/compare/v1.0.8...HEAD
[1.0.8]: https://github.com/szarblewski/SZA.AI/compare/v1.0.7...v1.0.8
[1.0.7]: https://github.com/szarblewski/SZA.AI/compare/v1.0.6...v1.0.7
[1.0.6]: https://github.com/szarblewski/SZA.AI/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/szarblewski/SZA.AI/compare/v1.0.4...v1.0.5
[1.0.4]: https://github.com/szarblewski/SZA.AI/compare/v1.0.3...v1.0.4
[1.0.3]: https://github.com/szarblewski/SZA.AI/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/szarblewski/SZA.AI/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/szarblewski/SZA.AI/compare/v1.0.0...v1.0.1
[2.0.1]: https://github.com/szarblewski/SZA.AI/compare/v2.0.0...v2.0.1
[2.0.0]: https://github.com/szarblewski/SZA.AI/compare/v1.1.0...v2.0.0
[1.1.0]: https://github.com/szarblewski/SZA.AI/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/szarblewski/SZA.AI/releases/tag/v1.0.0
