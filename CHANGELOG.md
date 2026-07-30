# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e
versionamento [SemVer](https://semver.org/lang/pt-BR/): `MAIOR.MENOR.CORREÇÃO`.

A versão vive em um único lugar: a constante `AppInfo.Version` em
[`src/Version.cs`](src/Version.cs). Ao mudá-la, adicione a seção correspondente aqui —
o `release.ps1` usa esta seção como corpo do release e se recusa a publicar sem ela.

## [Não lançado]

## [1.0.0] — 2026-07-30

Primeira versão pública.

### Adicionado

- Lista de processos com a memória de GPU de cada um, separando **VRAM dedicada
  residente** (contador `Local Usage`), **compartilhada** (`Non Local Usage`, o
  transbordo para a RAM do sistema) e **comprometida** (`Total Committed`).
- Painel com os **blocos alocados por segmento** físico do adaptador.
- Utilização por motor de GPU (3D, Compute, Copy, VideoDecode...) por processo.
- Cartões por adaptador com dedicada / compartilhada / total, e nomes reais vindos
  do DXGI em vez do LUID cru.
- Encerramento com **classificação de risco**: processos críticos do Windows são
  bloqueados (`IsProcessCritical` + lista de nomes), sistema e elevados exigem
  confirmação explícita, e o diálogo lista os serviços que caem junto.
- Fallback de `taskkill` via UAC quando o monitor não está elevado, e botão para
  reiniciar elevado sem deixar instância duplicada.
- Ícone na área de notificações mostrando a % de VRAM dedicada; o botão fechar
  minimiza para lá em vez de encerrar.
- **Ponte headless**: JSON completo gravado de forma atômica em
  `%LOCALAPPDATA%\VramMonitor\snapshot.json` a cada amostra, mais os modos de linha
  de comando `--json`, `--text`, `--watch`, `--headless` e `--kill`.
- Instância única: a segunda invocação traz a janela existente para a frente.
- Ordenação congela enquanto se lê a lista (ponteiro sobre ela ou scroll fora do
  topo), para as linhas não fugirem do cursor.
- Botão **♥ Doar** na barra e no menu da bandeja.
- Interface escura DPI-aware, compilada com o `csc.exe` do .NET Framework: um
  executável de ~100 KB, sem SDK e sem dependências.
