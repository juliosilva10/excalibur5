---
name: code
description: "Skill para construção de código seguindo princípios SOLID, boas práticas, código limpo, otimização de recursos e revisão pós-implementação."
---

Você está construindo código. Siga rigorosamente as diretrizes abaixo em todas as etapas.

## 1. Análise e Decomposição

Antes de escrever qualquer código:

- Entenda completamente o requisito. Se houver ambiguidade, pergunte.
- Se a tarefa for complexa, decomponha em subtarefas menores e independentes.
- Identifique dependências entre as subtarefas e defina a ordem de execução.
- Para cada subtarefa, defina critérios claros de conclusão.

## 2. Princípios SOLID (obrigatórios)

Aplique sempre:

- **S — Single Responsibility:** Cada classe/módulo/função tem uma única responsabilidade. Se faz mais de uma coisa, separe.
- **O — Open/Closed:** Entidades abertas para extensão, fechadas para modificação. Use abstrações, interfaces e composição.
- **L — Liskov Substitution:** Subtipos devem ser substituíveis por seus tipos base sem quebrar o comportamento.
- **I — Interface Segregation:** Interfaces pequenas e específicas. Nenhum cliente deve depender de métodos que não usa.
- **D — Dependency Inversion:** Dependa de abstrações, não de implementações concretas. Injete dependências.

## 3. Boas Práticas de Código

- Nomes descritivos e consistentes (variáveis, funções, classes).
- Funções curtas — idealmente até 20 linhas. Se ultrapassar, extraia.
- Evite aninhamento profundo (máximo 2-3 níveis). Use early return, guard clauses.
- Sem código morto, comentários obsoletos ou imports não utilizados.
- Sem valores mágicos — use constantes nomeadas.
- Tratamento de erros explícito e específico. Nunca engula exceções silenciosamente.
- Prefira imutabilidade quando possível.
- DRY (Don't Repeat Yourself) — mas sem abstrações prematuras.

## 4. Performance e Recursos

Priorize o uso eficiente de memória e hardware:

- **Memória:** Libere recursos quando não mais necessários. Evite retenção desnecessária de referências. Use estruturas de dados adequadas ao caso.
- **Concorrência:** Evite deadlocks — adquira locks em ordem consistente, prefira locks de escopo curto. Use timeouts quando apropriado.
- **Loops:** Nunca crie loops infinitos sem condição de saída garantida. Valide condições de parada.
- **I/O:** Feche streams, conexões e handles. Use padrões try-with-resources ou equivalentes.
- **Coleções:** Dimensione adequadamente. Evite cópias desnecessárias. Prefira lazy evaluation quando o dataset for grande.
- **Garbage:** Não crie objetos temporários em loops quentes. Reutilize buffers quando fizer sentido.

## 5. Segurança e Robustez

- Valide toda entrada externa (parâmetros, input do usuário, dados de API).
- Use queries parametrizadas — nunca concatene strings para SQL.
- Sanitize outputs quando relevante (XSS, injection).
- Não exponha informações sensíveis em logs ou mensagens de erro.
- Trate edge cases: null, vazio, limites numéricos, concorrência.

### 5.1 Proteção de Segredos e Dados Sensíveis

**Nunca** deixe vazar tokens, API keys, senhas ou variáveis de ambiente no código:

- **Variáveis de ambiente:** Use `.env` (ou equivalente do ecossistema) para armazenar segredos. Acesse via `process.env`, `os.environ`, `System.getenv()` ou mecanismo equivalente da linguagem.
- **Nunca hardcode:** Tokens, chaves de API, senhas, connection strings ou qualquer credencial não podem estar no código-fonte. Nem em comentários, nem em testes.
- **Arquivos de exemplo:** Se necessário documentar variáveis, crie um `.env.example` com valores placeholder (ex: `API_KEY=your_api_key_here`). Nunca com valores reais.
- **Logs e debug:** Nunca logue valores de segredos. Logue apenas que a variável existe ou está ausente, nunca seu conteúdo.
- **Commits:** Antes de commitar, verifique se nenhum segredo está sendo incluído. Se um segredo foi commitado acidentalmente, considere-o comprometido — rotacione imediatamente.

### 5.2 .gitignore (obrigatório)

Sempre que iniciar um projeto ou repositório, gere um `.gitignore` adequado à stack utilizada. No mínimo, inclua:

```
# Variáveis de ambiente e segredos
.env
.env.*
!.env.example

# Dependências
node_modules/
vendor/
venv/
__pycache__/
*.pyc

# Build e artefatos
dist/
build/
out/
target/
*.class
*.jar
*.war

# IDEs e editores
.idea/
.vscode/
*.swp
*.swo
.DS_Store
Thumbs.db

# Logs
*.log
logs/

# Arquivos de credenciais
*.pem
*.key
*.p12
*.jks
credentials.json
service-account.json
```

Adapte ao ecossistema do projeto (adicione entradas específicas para a linguagem/framework em uso). Se o projeto já possui um `.gitignore`, revise-o e complemente — nunca sobrescreva sem verificar.

## 6. Processo de Implementação

Para cada subtarefa:

1. **Implemente** seguindo os princípios acima.
2. **Compile/Execute** — garanta que não há erros.
3. **Elimine warnings** — todos, sem exceção.
4. **Verifique** que nenhuma funcionalidade existente foi quebrada.
5. **Teste** — escreva ou execute testes relevantes.

## 7. Revisão Final (obrigatória)

Após completar a implementação, faça uma revisão completa:

- [ ] Código compila sem erros e sem warnings?
- [ ] Todos os princípios SOLID estão sendo respeitados?
- [ ] Há código não utilizado (imports, variáveis, funções, classes)?
- [ ] Há possibilidade de memory leak, deadlock ou loop infinito?
- [ ] O tratamento de erros é adequado e específico?
- [ ] A feature nova não quebrou funcionalidades existentes?
- [ ] Há tokens, API keys, senhas ou segredos expostos no código?
- [ ] O `.gitignore` existe e cobre `.env`, credenciais e artefatos de build?
- [ ] Os nomes são claros e o código é legível sem comentários excessivos?
- [ ] A performance é aceitável para o caso de uso?

Se qualquer item falhar, corrija antes de entregar.

## 8. Regras de Ouro

- **Não adicione complexidade desnecessária.** Resolva o problema atual, não problemas hipotéticos futuros.
- **Não prejudique o existente.** Ao adicionar uma feature, rode os testes existentes. Se algo quebrou, corrija antes de prosseguir.
- **Código limpo > código esperto.** Legibilidade é prioridade.
- **Menos é mais.** Se pode resolver com menos código mantendo clareza, faça.
