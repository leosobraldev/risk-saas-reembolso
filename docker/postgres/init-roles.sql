-- Papel usado pela API em runtime: sem superuser e sem BYPASSRLS, portanto sujeito a Row-Level Security.
-- O usuario "riskguard" (dono/superuser) e usado APENAS pelo passo de migrations (AdminConnectionString / --migrate), nunca pela API.
-- Apenas para desenvolvimento local; em producao crie o papel com senha gerenciada por secret.
CREATE ROLE riskguard_app LOGIN PASSWORD 'riskguard_app_dev' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
GRANT CONNECT ON DATABASE riskguard TO riskguard_app;

-- Papel do Hangfire: so recebe permissoes no schema do Hangfire (concedidas pelo passo de migrations); nao ve os dados dos modulos.
CREATE ROLE riskguard_jobs LOGIN PASSWORD 'riskguard_jobs_dev' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
GRANT CONNECT ON DATABASE riskguard TO riskguard_jobs;
