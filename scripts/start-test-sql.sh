#!/usr/bin/env bash
# Starts a throwaway SQL Server 2022 container with a SQL-auth login for the integration tests
# and prints the environment variables that enable them.
set -euo pipefail
name="${CSVTOSQL_TEST_CONTAINER:-csvtosql-mssql}"
port="${CSVTOSQL_TEST_PORT:-14333}"
sa_password="${MSSQL_SA_PASSWORD:-Sa$(openssl rand -hex 12)Aa1!}"
user_password="${CSVTOSQL_TEST_SQL_PASSWORD:-Usr$(openssl rand -hex 12)Aa1!}"

docker rm -f "$name" >/dev/null 2>&1 || true
docker run -d --name "$name" -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=$sa_password" -p "$port:1433" \
  mcr.microsoft.com/mssql/server:2022-latest >/dev/null

for _ in $(seq 1 60); do
  if docker exec "$name" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$sa_password" -Q "SELECT 1" >/dev/null 2>&1; then
    break
  fi
  sleep 2
done

docker exec "$name" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$sa_password" -b -Q \
  "CREATE LOGIN CsvToDbUser WITH PASSWORD='$user_password', CHECK_POLICY=OFF; CREATE USER CsvToDbUser FOR LOGIN CsvToDbUser; ALTER ROLE db_owner ADD MEMBER CsvToDbUser;" >/dev/null

cat <<ENV
export CSVTOSQL_TEST_SQL_SERVER=localhost,$port
export CSVTOSQL_TEST_SQL_USER=CsvToDbUser
export CSVTOSQL_TEST_SQL_PASSWORD='$user_password'
ENV
