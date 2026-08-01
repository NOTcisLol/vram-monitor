# Modelo de ameaça e postura de segurança

Este documento é honesto sobre o que o VRAM Monitor **protege** e o que ele **não tem como
proteger**. Segurança que promete mais do que entrega é pior que nenhuma, porque leva a decisões
erradas.

## O que o programa não é

**Não é escada de privilégio.** O `--kill` chama `TerminateProcess` com o token de **quem
chamou**. Não há serviço, não há auto-elevação no manifesto (`asInvoker`) e não existe canal de
IPC pelo qual um processo comum mande a instância elevada agir. Um programa rodando como você já
podia encerrar os seus processos antes de o monitor existir — via `taskkill`, via WMI ou com três
linhas de código.

**O JSON não vaza segredo nem capacidade.** PID, nome e caminho de processo são enumeráveis por
qualquer código, sem privilégio:

```powershell
Get-Process | Select-Object Id, ProcessName, Path
```

E a própria fonte dos dados — os contadores `GPU Process Memory` — é legível por usuário comum:

```powershell
Get-Counter '\GPU Process Memory(*)\Local Usage'
```

Remover ou cifrar a ponte não tira nada de um atacante que já executa código na sua conta.

## Por que não há criptografia da ponte

Cifrar o arquivo e decifrar "só na interface" exige que a chave esteja acessível ao seu usuário —
que é exatamente o nível do atacante no cenário que motiva a criptografia. Ele lê a chave, ou nem
se dá o trabalho e lê o contador do Windows direto. Isso vale para qualquer esquema de
"registro de agentes" local: entre dois processos do **mesmo usuário** o sistema operacional não
oferece fronteira para atestar quem é o chamador.

O que existiria de real seria proteger o arquivo contra **outros usuários** da máquina. Isso já é
feito pela ACL de `%LOCALAPPDATA%`, que é por usuário.

## Vulnerabilidades reais encontradas e corrigidas

### 1. Elevação de privilégio pelo autostart de todos os usuários — corrigido

O recurso "iniciar com o Windows" no escopo **todos os usuários** grava em `shell:common startup`,
e o que está lá roda na sessão de **qualquer um que faça logon, inclusive administradores**.

O atalho apontava para o executável de onde o app estava rodando. Quando esse caminho fica em
pasta gravável pelo usuário (`Documents`, `Downloads`, Área de Trabalho — o caso comum), qualquer
código rodando **sem elevação** como você poderia trocar o binário. No boot seguinte, o trojan
executaria na sessão de todos os usuários da máquina. Ou seja: o recurso transformava execução
sem privilégio em execução na sessão de um administrador.

**Correção:** ao instalar no escopo de todos os usuários, o helper elevado verifica a ACL do
diretório de origem. Se qualquer identidade fora de `SYSTEM`, `Administradores`, `CREATOR OWNER` e
contas de serviço tiver permissão de escrita, o executável é **copiado para
`%ProgramFiles%\VRAM Monitor\`** e o atalho passa a apontar para lá. A verificação falha fechada:
se a ACL não puder ser lida, o diretório é tratado como inseguro.

O escopo **por usuário** não sofre disso — o atalho roda como você, que já controla o binário — e
por isso continua sem exigir elevação.

### 2. Sequestro de DLL — corrigido

O app resolve `pdh.dll`, `dxgi.dll` e `uxtheme.dll` por nome. Na ordem de busca padrão o diretório
do executável vem antes de `System32`, e o executável mora em pasta gravável. Plantar um
`pdh.dll` ao lado dele daria execução de código dentro do processo — grave quando o usuário roda
o monitor elevado.

**Correção:** `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)` logo no início do `Main`,
com `SetDllDirectory("")` como reserva em Windows mais antigo.

### 3. Reuso de PID matando processo inocente — corrigido

Entre a amostra e o clique, o processo pode morrer e o Windows reciclar o número para outro. O
código matava "o PID", não o processo que você viu na tela.

**Correção:** o horário de criação do processo é conferido imediatamente antes de
`TerminateProcess`. Se não bater, nada é encerrado e a interface avisa que o PID foi reciclado.

### 4. Injeção de argumento via caminho de processo — corrigido

"Abrir local do arquivo" concatenava o caminho em `explorer.exe /select,"..."`. O caminho vem de
**outro processo**, ou seja, é string controlada por terceiro: aspas nele injetariam argumentos.

**Correção:** o caminho é rejeitado se contiver aspas ou controles, precisa existir, e é passado
com `UseShellExecute = false`.

### 5. Uso do app como primitiva de escrita — corrigido

`--out` e `--jsonl` aceitavam caminho arbitrário, e parte do conteúdo gravado (nome e caminho de
processo) é controlada por terceiros. Dava para apontar a saída para uma pasta de inicialização,
ou para um `.bat`, e deixar um processo com nome forjado injetar a linha desejada.

**Correção:** extensões executáveis (`.exe`, `.bat`, `.cmd`, `.ps1`, `.lnk`, `.dll`, `.reg`, …) e
pastas sensíveis (inicialização, `System32`, `Windows`, `Program Files`, Menu Iniciar) são
recusadas antes de qualquer gravação.

### 6. Injeção de terminal por nome de processo — corrigido

O modo `--text` imprimia nomes de processo crus. Um processo com sequências ANSI no nome reescreve
o que aparece no terminal de quem lê a tabela. Caracteres de controle agora são removidos, tanto na
saída de texto quanto no log de auditoria.

## Endurecimento além das correções

- **Encerrar exige confirmação explícita, sempre.** A caixa de ciência no diálogo passou a ser
  obrigatória em **todos** os níveis de risco, não só para processos de sistema. Nenhum processo
  morre com um clique só.
- **Encerrar pela linha de comando exige UAC + diálogo.** O `--kill` sem elevação relança o
  próprio executável elevado; o filho mostra o diálogo de confirmação. São duas aprovações
  humanas. Nenhum flag pula isso — `--force` deixou de ser bypass e só vale para o fallback de
  acesso negado. O objetivo não é impedir o que o atacante já pode fazer sozinho, e sim tirar do
  app o papel de binário conveniente para automação hostil (*living off the land*).
- **Processos críticos continuam bloqueados**, e a checagem acontece **antes** de qualquer
  tentativa de elevação — pedir UAC para depois recusar seria treinar o usuário a clicar em "sim".
- **Log de auditoria** em `%LOCALAPPDATA%\VramMonitor\kills.log`: horário, origem (`ui`, `ui-uac`,
  `cli`), PID, nome, classificação de risco, se o monitor estava elevado e o resultado.

## Limites que continuam de pé

- Código rodando como você pode encerrar seus processos sem o monitor. Isso é uma propriedade do
  Windows, não deste programa.
- Um arquivo de idioma malicioso em `lang\` pode alterar textos da interface, inclusive os do
  diálogo de confirmação. Só carregue traduções de origem confiável.
- O executável não é assinado digitalmente. Confira o SHA-256 publicado no release, ou compile do
  fonte com `build.ps1`.
- Se você mantiver o binário em pasta gravável e rodar o monitor **elevado**, qualquer código
  rodando como você pode ter trocado esse binário antes. Para uso elevado rotineiro, mantenha o
  `.exe` em `Program Files`.

## Relatar um problema

Abra uma issue em https://github.com/NOTcisLol/vram-monitor/issues. Se for algo explorável,
descreva o impacto e o caminho — este é um utilitário local, sem dados de terceiros envolvidos.
