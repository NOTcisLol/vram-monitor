---
name: vram
description: >
  Ler quem está ocupando a VRAM da GPU no Windows pela ponte JSON do VRAM Monitor
  (%LOCALAPPDATA%\VramMonitor\snapshot.json), com fallback para VramMonitor.exe --json
  quando a janela não está aberta: memória dedicada residente por processo, transbordo
  para a RAM, blocos por segmento do adaptador, utilização por motor e encerramento
  seguro por PID. Use SEMPRE que o usuário perguntar quem está comendo a VRAM, se a
  placa está cheia, por que faltou memória de GPU, quanto um treino/jogo/navegador está
  usando, se dá para matar algo para liberar VRAM, ou pedir para acompanhar a memória da
  GPU ao longo do tempo — mesmo sem citar "VRAM Monitor" (ex.: "a GPU tá cheia?", "o que
  tá segurando memória da placa?", "consigo rodar mais um modelo?").
---

# VRAM Monitor — leitura da ponte

Dados de memória de GPU por processo no Windows. A fonte é o **VRAM Monitor**
(github.com/NOTcisLol/vram-monitor), que lê os contadores PDH do Windows — os mesmos do
Gerenciador de Tarefas, mas com a divisão por processo que ele não mostra.

## 1. Leia a ponte primeiro

O arquivo é reescrito a cada amostra enquanto a janela do monitor está aberta (mesmo
minimizada na bandeja). Ler custa zero processo novo:

```
%LOCALAPPDATA%\VramMonitor\snapshot.json
```

Use a ferramenta Read com o caminho já resolvido — em geral
`C:\Users\<usuário>\AppData\Local\VramMonitor\snapshot.json`. A gravação é atômica, então
nunca se vê conteúdo parcial.

**Confira o campo `timestamp` antes de usar.** Se estiver com mais de ~15 segundos, a
janela não está rodando e o arquivo é uma foto velha — vá para o passo 2. Nunca apresente
número velho como se fosse atual.

## 2. Fallback: sem a janela aberta

Rode o executável em modo headless. Ele funciona com a janela aberta ou fechada:

```bash
VramMonitor.exe --json --top 15
```

Leva cerca de 1 s (precisa de duas amostras para calcular a utilização dos motores).
Se `VramMonitor.exe` não estiver no PATH, procure na pasta do projeto — neste computador é
`C:\Users\conne\OneDrive\Documentos\Vram monitor\VramMonitor.exe` — e chame pelo caminho
completo, entre aspas por causa do espaço. Se o arquivo não existir, compile com
`build.ps1` na pasta do projeto ou baixe de
github.com/NOTcisLol/vram-monitor/releases/latest.

Outras formas, quando couberem melhor:

| Comando | Quando usar |
|---|---|
| `--text --top 15` | Tabela pronta para colar na resposta, sem JSON |
| `--watch --interval 2000 --count 10` | Acompanhar variação: um JSON por linha |
| `--headless --duration 300 --jsonl hist.jsonl` | Deixar rodando e ler o histórico depois |

## 3. Como interpretar (importante)

Ordene por `dedicatedBytes`. Os campos que importam, por processo:

| Campo | Significado |
|---|---|
| `dedicatedBytes` / `dedicatedMB` | **Residente na VRAM física. É quem realmente ocupa a placa.** |
| `sharedBytes` / `sharedMB` | Transbordou para a RAM do sistema. Conta no total de memória de GPU, mas não ocupa VRAM. |
| `totalGpuBytes` | Soma dos dois — equivale à "Memória da GPU" do Gerenciador de Tarefas. |
| `committedBytes` | Tudo que o processo reservou. **Não use para dizer quem encheu a VRAM.** |
| `gpuPercent` / `topEngine` | Utilização do motor mais ativo (3D, Compute, Copy, VideoDecode...). |
| `blocks[]` | Blocos por segmento físico do adaptador. |

**A armadilha do `committed`:** uma alocação compartilhada é contada em *cada* processo que
a referencia, então a soma pode passar do total físico e o `dwm.exe` costuma aparecer com
dezenas de GB comprometidos tendo só algumas centenas de MB residentes. Se a pergunta é
"quem está enchendo a VRAM", a resposta sai de `dedicatedBytes`.

Para o estado real da placa use `adapters[]`: `dedicatedUsedBytes` / `dedicatedTotalBytes`
é a pressão de verdade. `totals.dedicatedBytes` é a soma dos processos e fica um pouco
abaixo do valor do adaptador, que inclui memória reservada pelo driver.

## 4. Encerrar um processo

Só quando o usuário pedir explicitamente, e sempre confirmando **nome + PID** antes.
Nunca escolha a vítima sozinho.

```bash
VramMonitor.exe --kill 1234
```

O próprio executável já aplica as travas — não tente contorná-las:

| Saída | Significado | O que fazer |
|---|---|---|
| `0` | Encerrado | Confirmar e reler a ponte |
| `3` | Processo **crítico** do Windows (BSOD/queda de sessão) | **Parar.** Nem `--force` libera, e está certo assim |
| `4` | Sistema ou elevado, exige confirmação | Explicar o risco (o JSON traz `services[]`, que cai junto) e só repetir com `--force` se o usuário confirmar |
| `5` | PID não existe mais | Reler a ponte |
| `6` | Acesso negado | Precisa do monitor elevado, ou `--force` para disparar o UAC |

O campo `killBlocked` do JSON antecipa o caso crítico, e `risk` (`user`/`elevated`/
`system`/`critical`) diz o quanto pedir de confirmação.

## 5. Ao responder

Comece pelo estado do adaptador (usado/total e a porcentagem), depois liste os maiores
consumidores por VRAM dedicada — em geral 5 a 10 linhas bastam. Cite o horário da amostra
quando a leitura veio da ponte. Se um processo tem `sharedBytes` alto, vale dizer: ele já
transbordou para a RAM, o que costuma significar queda de desempenho, não folga.
