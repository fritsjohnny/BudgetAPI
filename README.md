# 💰 BudgetAPI

> Backend REST em **ASP.NET Core (.NET 8)** para controle financeiro pessoal  
> Projeto público focado em **arquitetura**, **segurança** e **uso real em produção**

---

## 🚀 O que é este projeto

O **BudgetAPI** é o backend de um sistema financeiro completo, responsável por:

💼 Contas e saldos  
📥 Receitas e despesas  
💳 Cartões de crédito e faturas  
🏷️ Categorias e pessoas  
📈 Rendimentos (yields)  
🔔 Notificações automáticas  

> Não é um projeto acadêmico.  
> Foi pensado, estruturado e executado como **sistema de produção**.

---

## 🧱 Stack principal

| Camada | Tecnologia |
|------|-----------|
| API | ASP.NET Core (.NET 8) |
| ORM | Entity Framework Core |
| Banco | SQL Server |
| Auth | JWT (middleware customizado) |
| Background | Hosted Services |
| Push | Firebase Cloud Messaging |
| Cloud | Azure App Service |

---

## 🔐 Segurança (ponto-chave do projeto)

✔️ Nenhum secret versionado  
✔️ Configurações sensíveis fora do código  
✔️ Histórico Git **sanitizado** antes de tornar público  
✔️ Swagger habilitado **somente em Development**  
✔️ Endpoints de teste isolados do ambiente produtivo  

**Configuração por ambiente**
- Local → `secrets.json`
- Produção → Azure Application Settings

### Importação de faturas C6

O importador lê o CSV dentro do ZIP protegido do C6. Cadastre a senha da fatura no cartão; a API protege a senha antes de armazená-la e a descriptografa apenas durante a importação. A configuração global antiga continua disponível como fallback:

```powershell
dotnet user-secrets set "C6Statement:ZipPassword" "<senha do ZIP>" --project BudgetAPI.csproj
```

No Azure, configure a chave `C6Statement:ZipPassword` em Application Settings. A tabela de cartões físicos, a associação opcional em `CardsPostings` e os apelidos de descrição são criados pelo script idempotente `Scripts/20261013_AddPhysicalCardsAndStatementAliases.sql`.

As senhas por cartão são protegidas com ASP.NET Core Data Protection. Em produção, mantenha o key ring persistente e compartilhado entre instâncias da API para que as senhas continuem legíveis após reinícios e escalabilidade horizontal.

---

## 📧 Confirmação de e-mail e recuperação de senha

Antes de publicar a versão que exige e-mail em novas contas, aplique
`Scripts/20261028_AddAccountContactFields.sql` no banco SQL Server.

Configure no ambiente da API:

- `BUDGETAPI_EMAIL_FROM`: endereço remetente autorizado pelo SMTP.
- `BUDGETAPI_SMTP_USER` e `BUDGETAPI_SMTP_PASS`: credenciais SMTP já usadas pelos serviços de e-mail.

Os links de confirmação e redefinição apontam para a URL de produção do frontend, definida em `Services/AccountEmailService.cs`.

Os links usam Angular hash routing. Tokens de confirmação e redefinição são aleatórios, de uso único, armazenados no banco somente como SHA-256 e expiram em 24 horas e 30 minutos, respectivamente. A troca autenticada exige a senha atual. Após trocar ou redefinir a senha, a versão da credencial invalida JWTs emitidos anteriormente.

## ⚙️ Decisões de arquitetura

🔹 Controllers enxutos  
🔹 Regras concentradas em `Services`  
🔹 Separação clara de responsabilidades  
🔹 Background jobs com Hosted Services  
🔹 Logs explícitos e rastreáveis  
🔹 Nada “mágico” ou implícito  

---

## 🗂️ Organização do código

- **Controllers/** → Endpoints REST  
- **Services/** → Regras de negócio  
- **Models/** → Entidades e DTOs  
- **Authorization/** → JWT e autorização  
- **Helpers/** → Middlewares e utilitários  
- **SQLs/** → Scripts auxiliares  

---

## 📌 Por que este repositório é público

Este projeto foi tornado público **como portfólio técnico**.

- Backups de banco ❌
- Perfis de publicação ❌
- Credenciais ❌
- Dados reais ❌

O foco aqui é **arquitetura, decisões técnicas e maturidade profissional**.

---

## 👤 Autor

**Johnny Frits**  
Senior .NET Backend / Full Stack Developer  

🔹 APIs REST  
🔹 Sistemas críticos  
🔹 Integrações  
🔹 Arquitetura limpa  

