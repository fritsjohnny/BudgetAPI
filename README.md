```md
# BudgetAPI

API REST desenvolvida em **ASP.NET Core (.NET 8)** para gestão financeira pessoal, servindo como backend de um ecossistema de aplicações (web e mobile).  
Projeto público com foco em **arquitetura**, **boas práticas**, **segurança** e **robustez em produção**.

---

## Visão geral

O **BudgetAPI** centraliza regras de negócio e persistência de dados para um sistema de controle financeiro, cobrindo:

- Contas e saldos
- Receitas e despesas
- Cartões de crédito e faturas
- Categorias e pessoas
- Rendimentos (yields)
- Notificações automáticas

O projeto foi estruturado pensando em **uso real em produção**, não apenas como demo.

---

## Principais características técnicas

- **ASP.NET Core (.NET 8)**
- **Entity Framework Core**
- **SQL Server**
- **JWT Authentication** (middleware customizado)
- **Hosted Services** para tarefas em background
- **Firebase Cloud Messaging (FCM)** para push notifications
- **Azure App Service** (produção)
- **Configuração segura** via Azure Application Settings / User Secrets
- **Swagger habilitado apenas em Development**

---

## Destaques de arquitetura

### 🔐 Segurança
- Nenhum secret versionado no repositório
- Chaves e credenciais:
  - Local: `secrets.json`
  - Produção: **Azure App Settings**
- Histórico Git **revisado e sanitizado** (sem backups, sem dados sensíveis)
- Endpoints de teste isolados do ambiente de produção

### ⚙️ Configuração consciente
- Separação clara entre:
  - ambiente local
  - ambiente de produção
- Fail fast para configurações críticas (ex.: connection strings)
- Nada “mágico” ou escondido

### 🔄 Background processing
- Hosted Services para:
  - Keep-alive da API
  - Processamento e envio de notificações
- Execução controlada, com logs claros e rastreáveis

---

## Organização do projeto

- `Controllers/` — Endpoints REST
- `Services/` — Regras de negócio
- `Models/` — Entidades e DTOs
- `Authorization/` — Autenticação e autorização JWT
- `Helpers/` — Middlewares e utilitários
- `SQLs/` — Scripts auxiliares (sem dados sensíveis)

---

## O que este repositório demonstra

- Capacidade de **pensar em produção**
- Cuidado com **segurança e histórico Git**
- Organização de código e responsabilidades
- Experiência prática com **Azure**, **.NET**, **APIs REST** e **background services**
- Decisões técnicas conscientes (e documentadas)

---

## Observação importante

Este repositório é público **para fins de portfólio**.  
Dados reais, backups, perfis de publicação e credenciais **não fazem parte do código versionado**.

---

## Autor

Johnny Frits  
Senior .NET Backend / Full Stack Developer  
Foco em APIs, sistemas críticos, integrações e arquitetura limpa
```
