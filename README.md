# Monitor de VRAM

[![release](https://img.shields.io/github/v/release/NOTcisLol/vram-monitor?label=vers%C3%A3o)](https://github.com/NOTcisLol/vram-monitor/releases/latest)
[![downloads](https://img.shields.io/github/downloads/NOTcisLol/vram-monitor/total?label=downloads)](https://github.com/NOTcisLol/vram-monitor/releases)
![windows](https://img.shields.io/badge/Windows-10%201709%2B%20%7C%2011-0078D6)

### ⬇ [Baixar VramMonitor.exe](https://github.com/NOTcisLol/vram-monitor/releases/latest/download/VramMonitor.exe)

Arquivo único de ~100 KB, sem instalador e sem dependências — esse link sempre aponta para a
versão mais recente. Na primeira execução o SmartScreen pode avisar que o publicador é
desconhecido (o executável não é assinado): *Mais informações* → *Executar assim mesmo*. Quem
preferir não confiar no binário [compila o próprio](#compilar) em segundos.

---

Mostra **qual processo está ocupando a VRAM** da GPU, com o tamanho dos blocos alocados por
segmento do adaptador, e permite encerrar o processo com travas de segurança para processos
do sistema, elevados e críticos.

Lê os mesmos contadores de desempenho que o Gerenciador de Tarefas usa (`GPU Process Memory`,
`GPU Engine`, `GPU Adapter Memory`), mas expõe o que ele esconde: a divisão por processo entre
memória residente na placa, transbordo para a RAM, memória comprometida e blocos por segmento.

## Compilar

Não precisa de SDK — usa o `csc.exe` que já vem no Windows (.NET Framework 4).

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

Gera `VramMonitor.exe` (~100 KB, sem dependências). `-Run` compila e executa.

## Versionamento

A versão vive num único lugar: a constante `AppInfo.Version` em [`src/Version.cs`](src/Version.cs).
Dela derivam o `AssemblyVersion`/`FileVersion` do executável (visível nas propriedades do
arquivo), o título da janela, o `--version`, o cabeçalho da ajuda e o campo `appVersion` do JSON.
As mudanças de cada versão ficam no [CHANGELOG.md](CHANGELOG.md), seguindo
[SemVer](https://semver.org/lang/pt-BR/).

Para lançar uma versão nova:

```bash
powershell -ExecutionPolicy Bypass -File release.ps1
```

O script lê a versão de `src/Version.cs`, exige a seção correspondente no `CHANGELOG.md`
(que vira o corpo do release), recusa lançar com alterações não commitadas, compila, confere
que o `FileVersion` do binário casa com a versão declarada, calcula o SHA-256 e cria a tag e o
release com o `.exe` anexado. `-DryRun` mostra tudo sem publicar.

## Como ler os números

| Coluna | Contador | O que é |
|---|---|---|
| **VRAM dedicada** | `Local Usage` | Bytes residentes na memória física da placa. **É o que realmente ocupa a VRAM.** |
| **Compartilhada** | `Non Local Usage` | Transbordo para a RAM do sistema. Faz parte do total de memória da GPU, mas não ocupa VRAM física. |
| **Total GPU** | soma das duas | Equivale a "Memória da GPU" no Gerenciador de Tarefas. |
| **Comprometido** | `Total Committed` | Tudo que o processo reservou, incluindo blocos compartilhados entre processos e paginados. |
| **GPU / Motor** | `GPU Engine` | Utilização do motor mais ativo (3D, Compute, Copy, VideoDecode...). |

**Cuidado com "Comprometido":** a soma de todos os processos pode passar do total físico,
porque uma alocação compartilhada é contada em cada processo que a referencia. É por isso que
o `dwm.exe` às vezes aparece com dezenas de GB comprometidos enquanto tem só algumas centenas
de MB realmente residentes. Para saber quem está enchendo a VRAM, use **VRAM dedicada**.

O painel inferior mostra os blocos por segmento físico do adaptador — a maior granularidade
que o Windows expõe sem rastreamento por ETW no kernel.

## Segurança ao encerrar

| Classificação | Critério | Comportamento |
|---|---|---|
| **CRÍTICO** | `IsProcessCritical`, PID ≤ 4 ou nome na lista (`System`, `csrss`, `lsass`, `winlogon`, `services`, `smss`, `wininit`...) | **Bloqueado.** Encerrar causaria BSOD ou queda da sessão. Nem `--force` libera. |
| **Sistema** | conta SYSTEM/LocalService/NetworkService, sessão 0, ou hospeda serviços | Exige marcar a caixa de confirmação; o diálogo lista os serviços que caem junto. |
| **Elevado** | token de administrador, ou processo que não pôde ser aberto para consulta | Exige confirmação; se o encerramento direto falhar, oferece `taskkill` via UAC. |
| **Usuário** | processo comum da sessão atual | Confirmação simples. |

Processos que o Windows reinicia sozinho (`dwm`, `explorer`, `audiodg`, `sihost`...) trazem uma
nota explicando o efeito real de encerrá-los.

## Interface

- **Atalhos:** `Del` matar · `Ctrl+C` copiar `taskkill /F /PID n` · `F5` atualizar ·
  `Espaço` pausar · `Ctrl+F` filtro · `F1` ajuda · duplo-clique abre a confirmação.
- **Ordem congelada:** com o ponteiro sobre a lista, ou com o scroll fora do topo, as linhas
  param de trocar de lugar (os valores continuam atualizando). Volte ao topo e tire o mouse
  para retomar o ranking ao vivo.
- **Bandeja:** o botão fechar (X) minimiza para a área de notificações e o monitor continua
  rodando; o ícone mostra a % de VRAM dedicada. `Sair` no menu da bandeja encerra de verdade.
  No Windows 11 o ícone nasce escondido no overflow (`^`) — para fixá-lo:
  *Configurações → Personalização → Barra de tarefas → Outros ícones da bandeja do sistema*.
- **Instância única:** abrir o app uma segunda vez não cria outra janela — apenas traz a
  existente para a frente (mesmo se ela estiver escondida na bandeja) e sai. Se a instância
  em execução for elevada, um aviso indica onde encontrá-la, porque o Windows bloqueia
  mensagens de um processo de integridade menor para um maior.
- **Elevação:** o app roda sem privilégios; o botão `Elevar` reinicia como administrador,
  necessário para encerrar processos elevados sem passar pelo UAC a cada vez. A instância
  antiga é encerrada de verdade (não fica escondida na bandeja), e a trava de instância única
  é liberada antes de iniciar a cópia elevada. Se o UAC for recusado, nada é fechado.
- Os modos headless (`--json`, `--text`, `--watch`, `--kill`) **não** passam pela trava:
  funcionam com a janela aberta ou fechada.

## Idiomas

**Inglês, português, espanhol, francês e alemão** vêm embutidos no executável — o download
continua sendo um arquivo único. O seletor é o botão com o código do idioma na barra
(`EN ▾`), a troca é imediata e a escolha fica salva. O padrão é inglês; há também a opção
*Automático*, que segue o idioma do Windows.

O idioma escolhido também governa a **formatação numérica**: em inglês `1.86 GB`, em
português `1,86 GB`.

Para acrescentar um idioma ou corrigir uma tradução sem recompilar, coloque um JSON em
`lang\` ao lado do `.exe` ou em `%LOCALAPPDATA%\VramMonitor\lang`:

```bash
copy lang\en-US.json "%LOCALAPPDATA%\VramMonitor\lang\it-IT.json"
```

Edite `meta.code`, `meta.nativeName` e `meta.culture`, traduza os valores e reabra o app —
o novo idioma aparece no seletor. Um arquivo com o mesmo `meta.code` de um embutido
substitui o embutido, e chaves que faltarem caem no inglês, então uma tradução parcial já
funciona. Erro de sintaxe é reportado na abertura.

Contribuições de tradução são bem-vindas: os arquivos ficam em [`lang/`](lang/) e todos têm
o mesmo conjunto de 188 chaves de `en-US.json`, que é a referência.

## Ponte headless

Enquanto a janela está aberta (mesmo minimizada na bandeja), cada amostra grava um JSON
completo em:

```
%LOCALAPPDATA%\VramMonitor\snapshot.json
```

A gravação é atômica, então um leitor nunca vê conteúdo parcial. Dá para desligar no menu da
bandeja. É o caminho mais barato para um script ou agente acompanhar a VRAM: basta ler o arquivo.

Sem janela nenhuma, a linha de comando faz o mesmo:

```bash
VramMonitor.exe --json                        # um snapshot JSON no stdout
VramMonitor.exe --text --top 10               # tabela legível
VramMonitor.exe --watch --interval 2000       # um JSON por linha, contínuo
VramMonitor.exe --headless --jsonl hist.jsonl # sem UI, mantendo o arquivo + histórico
VramMonitor.exe --kill 1234 --force           # encerra com as mesmas travas
VramMonitor.exe --help
```

Opções: `--interval MS`, `--count N`, `--duration S`, `--top N`, `--min-mb N`, `--out PATH`,
`--jsonl PATH`, `--compact`, `--warmup MS`.

Códigos de saída do `--kill`: `0` ok · `3` processo crítico (bloqueado) · `4` exige `--force` ·
`5` processo não existe · `6` acesso negado · `1` outra falha.

### Formato do JSON

```jsonc
{
  "schema": 1,
  "timestamp": "2026-07-29T22:32:33-03:00",
  "source": "gui",              // gui | cli | watch | headless
  "monitorElevated": false,
  "adapters": [{
    "luid": "0x00000000_0x00012D76",
    "name": "AMD Radeon RX 7600",
    "dedicatedTotalBytes": 8529100800, "dedicatedUsedBytes": 7989477376,
    "dedicatedUsedMB": 7619.4, "dedicatedPercent": 93.7,
    "sharedTotalBytes": 21432057856, "sharedUsedBytes": 2194993152,
    "gpuMemoryTotalBytes": 29961158656, "gpuMemoryUsedBytes": 10184470528
  }],
  "processes": [{
    "pid": 5228, "name": "python",
    "dedicatedBytes": 6056837120, "dedicatedMB": 5776.2,   // residente na VRAM
    "sharedBytes": 1332936704,   "sharedMB": 1271.2,       // transbordo para a RAM
    "totalGpuBytes": 7389773824, "committedBytes": 7389773824,
    "gpuPercent": 99.7, "topEngine": "Compute 0",
    "risk": "user",              // user | elevated | system | critical | unknown
    "killBlocked": false, "elevated": false, "session": 1,
    "user": "APPC\\conne", "path": "C:\\...\\python.exe",
    "killCommand": "taskkill /F /PID 5228",
    "services": [],
    "engines": [{ "engine": "Compute 0", "percent": 99.7 }],
    "blocks":  [{ "luid": "0x...", "segment": 0, "dedicatedBytes": ..., "sharedBytes": ... }]
  }],
  "totals": { "processCount": 32, "dedicatedMB": 7152.9, "sharedMB": 2093.6,
              "adapterDedicatedPercent": 92.5 }
}
```

Processos vêm ordenados por `dedicatedBytes` decrescente. Strings são ASCII puro
(non-ASCII vira `\uXXXX`), então a saída passa por qualquer codepage de console.

## Estrutura

| Arquivo | Responsabilidade |
|---|---|
| `src/Native.cs` | P/Invoke: PDH, APIs de processo/token, DXGI |
| `src/GpuSampler.cs` | Consulta PDH, agrega por PID/adaptador/segmento, nomes via DXGI |
| `src/ProcessCatalog.cs` | Metadados, elevação, criticidade, serviços (WMI), encerramento |
| `src/JsonExport.cs` | Leitor e escritor JSON, serialização do snapshot (a ponte) |
| `src/Cli.cs` | Modos headless |
| `src/MainForm.cs` | Janela, lista, bandeja, atalhos |
| `src/I18n.cs`, `lang/*.json` | Idiomas da interface (inglês é a referência) |
| `src/Settings.cs` | Preferências em %LOCALAPPDATA% |
| `src/AdapterHeader.cs`, `src/KillConfirmForm.cs`, `src/Theme.cs`, `src/TrayGauge.cs` | UI |
| `src/SingleInstance.cs` | Trava de instância única e sinalização da janela existente |
| `src/Version.cs` | Versão (fonte única) e metadados do executável |

O código é C# 5 (limite do `csc.exe` do .NET Framework) e todo literal em pixels passa por
`Dpi.S()`, porque o app é system-DPI-aware.

## Apoiar

Se o monitor te economizou tempo, dá para apoiar o desenvolvimento pelo botão **♥ Doar** na
barra de ferramentas (também no menu da bandeja) ou direto no link:

**https://link.mercadopago.com.br/donatedev**

## Requisitos

Windows 10 1709+ (ou 11) com driver WDDM 2.x — é quando os contadores `GPU Process Memory`
passaram a existir. Sem eles o app avisa na abertura.
