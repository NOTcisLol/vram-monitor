# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e
versionamento [SemVer](https://semver.org/lang/pt-BR/): `MAIOR.MENOR.CORREÇÃO`.

A versão vive em um único lugar: a constante `AppInfo.Version` em
[`src/Version.cs`](src/Version.cs). Ao mudá-la, adicione a seção correspondente aqui —
o `release.ps1` usa esta seção como corpo do release e se recusa a publicar sem ela.

## [Não lançado]

## [1.3.0] — 2026-07-31

Revisão de segurança. Detalhes e modelo de ameaça em [SECURITY.md](SECURITY.md).

### Corrigido

- **Elevação de privilégio pelo autostart de todos os usuários.** O atalho em
  `shell:common startup` roda na sessão de qualquer um que faça logon, inclusive
  administradores, e apontava para o executável de onde o app rodava — tipicamente uma pasta
  gravável pelo usuário. Qualquer código **sem elevação** podia trocar o binário e ganhar
  execução na sessão de todos no boot seguinte. Agora o helper elevado verifica a ACL do
  diretório e, se ele não for restrito a administradores, copia o executável para
  `%ProgramFiles%\VRAM Monitor\` antes de criar o atalho. A verificação falha fechada.
- **Sequestro de DLL.** `pdh.dll`, `dxgi.dll` e `uxtheme.dll` eram resolvidas por nome, e o
  diretório do executável precede `System32` na ordem de busca. Agora o `Main` chama
  `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)`.
- **Reuso de PID.** Entre a amostra e o clique o PID pode ter sido reciclado; o código matava
  o número, não o processo mostrado. O horário de criação passa a ser conferido imediatamente
  antes de `TerminateProcess`.
- **Injeção de argumento** em "abrir local do arquivo": o caminho vem de outro processo e era
  concatenado em `explorer.exe /select,"..."`. Agora é validado e passado sem shell.
- **Uso como primitiva de escrita.** `--out` e `--jsonl` aceitavam qualquer caminho, com
  conteúdo parcialmente controlado por terceiros (nomes de processo). Extensões executáveis e
  pastas sensíveis passam a ser recusadas.
- **Injeção de terminal** por nome de processo com sequências ANSI na saída `--text`.

### Alterado

- A caixa de ciência no diálogo de encerramento é **obrigatória em todos os níveis de risco**.
  Nenhum processo morre com um clique só.
- `--kill` pela linha de comando exige **UAC mais o diálogo de confirmação**: duas aprovações
  humanas. `--force` deixou de ser bypass e só vale para o fallback de acesso negado. A recusa
  de processo crítico acontece antes de qualquer tentativa de elevação.

### Adicionado

- Log de auditoria em `%LOCALAPPDATA%\VramMonitor\kills.log` com horário, origem, PID, nome,
  risco, estado de elevação e resultado.
- `SECURITY.md` com o modelo de ameaça, incluindo o que o programa **não** protege e por que
  cifrar a ponte com chave local não seria uma barreira.

## [1.2.0] — 2026-07-30

### Adicionado

- **Iniciar com o Windows**, no menu da bandeja. Ao ligar, o app tenta primeiro a pasta de
  inicialização **comum** (`shell:common startup`, vale para todos os usuários) relançando-se
  elevado — o que dispara o UAC. Se a elevação for recusada ou a gravação falhar, um popup
  oferece instalar **só para o seu usuário** (`shell:startup`), que não exige elevação
  nenhuma. O item mostra o escopo ativo e desmarcar remove o atalho, pedindo UAC de novo se
  ele estiver no escopo de todos os usuários.
- O atalho aponta para o executável com `--tray`, então o monitor sobe direto na área de
  notificações em vez de abrir a janela na cara de quem ligou o computador.
- Botão **⚙** na barra de ferramentas, abrindo o mesmo menu de opções da bandeja (iniciar
  com o Windows, ponte JSON, copiar/abrir o caminho do JSON). Sem ele o início automático
  ficava só na bandeja, que o Windows 11 esconde no overflow por padrão — ou seja, invisível.
- Modos de linha de comando `--install-startup` e `--uninstall-startup`, com `--all-users`.
  São o que a interface executa elevada, e também servem para scriptar a instalação.
- Skill do Claude Code em [`.claude/skills/vram`](.claude/skills/vram/SKILL.md): lê a ponte
  JSON, cai no modo headless quando a ponte está velha e documenta como interpretar os
  números e os códigos de saída do `--kill`.

### Notas

- O atalho é criado via `IShellLink` (COM), não via `WScript.Shell`: o Windows Script Host
  pode estar desabilitado por política e o recurso morreria sem motivo.
- Nos modos disparados pela interface via UAC o app não cria console próprio — sem isso uma
  janela preta piscava durante a elevação.

## [1.1.0] — 2026-07-30

### Adicionado

- **Idiomas.** Todo o texto da interface saiu do código e foi para arquivos JSON, um por
  idioma, com **inglês, português, espanhol, francês e alemão** embutidos no executável.
  O seletor fica na barra (botão com o código do idioma, ex. `EN ▾`) e a troca é imediata,
  sem reiniciar. **Inglês é o padrão**; há também a opção *Automático*, que segue o idioma
  do Windows.
- Qualquer JSON solto em `lang\` ao lado do `.exe` — ou em `%LOCALAPPDATA%\VramMonitor\lang` —
  é carregado junto e pode acrescentar um idioma novo ou substituir um embutido (mesmo
  `meta.code`) sem recompilar. Chaves que faltarem caem no inglês, e erro de sintaxe num
  arquivo da comunidade é avisado na abertura em vez de falhar em silêncio.
- O idioma escolhido também define a **formatação numérica**: sem isso a interface em
  inglês mostraria `5,64 GB` com vírgula decimal numa máquina configurada em português.
- Preferências persistidas em `%LOCALAPPDATA%\VramMonitor\settings.json` (idioma, intervalo
  de amostragem, filtro "só com uso de GPU" e a ponte JSON).
- Botão **♥ Doar** na barra de ferramentas e **♥ Apoiar o projeto** no menu da bandeja.
- Leitor JSON próprio, escrito à mão: o app não passa a depender de `System.Web.Extensions`
  nem de qualquer assembly que possa faltar na máquina do usuário.

### Corrigido

- A faixa após a última coluna das listas mostrava o cabeçalho branco do tema nativo no meio
  da interface escura; agora a última coluna preenche a largura restante.
- A coluna de VRAM dedicada cortava o próprio título quando a seta de ordenação aparecia.
- O estado de elevação saiu da barra de ferramentas para o rodapé: na barra ele disputava
  espaço com os botões e desaparecia em alguns idiomas.

### Alterado

- O nome do produto passou a ser **VRAM Monitor** (marca, não traduzida); o subtítulo da
  janela é que muda de idioma.
- O layout da barra de ferramentas é calculado a partir das larguras reais dos textos, em vez
  de posições fixas — necessário porque cada idioma tem rótulos de tamanho diferente.

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
