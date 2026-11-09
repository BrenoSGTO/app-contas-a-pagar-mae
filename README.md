<img src="assets/icon.svg" width="64" height="64" alt="Ícone Contas a Pagar">

# 📋 Contas a Pagar da Mãe — Planilha Inteligente

Aplicativo para computador (Windows) simples, visual e intuitivo em formato de **planilha** para controle de contas recorrentes e parceladas.

## 📥 Download do Instalador

👉 **[CLIQUE AQUI PARA BAIXAR O INSTALADOR (`Instalador-ContasAPagar.zip`)](https://github.com/BrenoSGTO/app-contas-a-pagar-mae/releases/latest/download/Instalador-ContasAPagar.zip)**

### Como instalar no computador:
1. Clique no link acima para baixar o arquivo **`Instalador-ContasAPagar.zip`**.
2. Abra o arquivo `.zip` baixado e dê dois cliques em **`instalador.exe`**.
3. Clique no botão verde **"Instalar Agora"**.
4. Pronto! O programa será instalado e criará automaticamente o atalho **"Contas a Pagar"** na sua Área de Trabalho.

---

## ✨ Funcionalidades Principais

- **✔ Checkmarks de Pagamento Mensal**: Marque com um único clique na coluna `✔ PAGA?` se a conta daquele mês já foi paga.
- **📅 Datas de Pagamento Claras**: Exibe a data completa de vencimento (`dd/MM/aaaa`) de cada conta no mês selecionado e alerta se alguma conta vence hoje ou está atrasada.
- **🔢 Quantidade de Recorrências e Mês do 1º Pagamento**: Ao cadastrar uma nova conta, escolha:
  - O **dia de vencimento**;
  - O **mês e ano do primeiro pagamento**;
  - A **quantidade de recorrências (parcelas/meses)** que a conta vai durar — ou marque como **conta fixa contínua** (ex: Luz, Água, Internet).
- **📊 Visual de Planilha Mensal + Matriz Anual (Jan a Dez)**:
  - **Aba Planilha Mensal**: Veja todas as contas do mês, filtre por *Todas*, *Pendentes* ou *Pagas*, edite valores diretamente na célula se o valor do mês variar e veja os cartões de resumo (*Total do Mês*, *Já Pago*, *Falta Pagar*).
  - **Aba Visão Anual**: Veja os 12 meses lado a lado como uma planilha anual e marque/desmarque pagamentos clicando diretamente no mês.
- **🔄 Atualizações Automáticas pelo GitHub**: Botão **"Verificar Atualizações"** no topo do programa que consulta o GitHub, baixa a versão nova automaticamente e atualiza o aplicativo sem perder nenhuma conta cadastrada.

---

## 🛠️ Como publicar atualizações futuras para sua mãe

Sempre que você fizer melhorias ou corrigir bugs:
1. Atualize o número da versão `AppVersion` em [`src/ContasAPagar.cs`](src/ContasAPagar.cs) (ex: `"1.0.1"`).
2. Rode o script de compilação:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   ```
3. Publique uma nova Release no GitHub (ou crie uma tag `v1.0.1` que o GitHub Actions compila e publica sozinho):
   ```powershell
   gh release create v1.0.1 dist/Instalador-ContasAPagar.zip dist/ContasAPagar.exe --title "Versão 1.0.1" --notes "Melhorias no programa"
   ```
4. No computador da sua mãe, basta ela clicar em **"🔄 Verificar Atualizações"** dentro do programa!
