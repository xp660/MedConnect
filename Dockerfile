# MedConnect API 映像（architecture-plan.md §0 v1.13）。
# 映像標籤釘到 patch 版本（與 CI 釘 runner 同一個原則：環境不會在我們不知情時改變；升級時手動改這兩行）。
# 標籤與使用者／port 都用實際映像驗證過，不是憑記憶：
#   aspnet:9.0.21-bookworm-slim 內有使用者 app（uid=1654），APP_UID=1654、ASPNETCORE_HTTP_PORTS=8080；
#   映像預設的 User 是 root，所以下面必須明確 USER。

FROM mcr.microsoft.com/dotnet/sdk:9.0.319-bookworm-slim AS build
WORKDIR /src

# 先只複製 csproj 並 restore：只要套件清單沒變，這一層就能被快取，改程式碼不會重新下載套件。
COPY src/MedConnect.Domain/MedConnect.Domain.csproj src/MedConnect.Domain/
COPY src/MedConnect.Application/MedConnect.Application.csproj src/MedConnect.Application/
COPY src/MedConnect.Infrastructure/MedConnect.Infrastructure.csproj src/MedConnect.Infrastructure/
COPY src/MedConnect.Api/MedConnect.Api.csproj src/MedConnect.Api/
RUN dotnet restore src/MedConnect.Api/MedConnect.Api.csproj

COPY src/ src/
RUN dotnet publish src/MedConnect.Api/MedConnect.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false \
    # Development 專用的設定（本機資料庫連線字串）不屬於映像；容器的設定一律來自環境變數。
    && rm -f /app/publish/appsettings.Development.json

FROM mcr.microsoft.com/dotnet/aspnet:9.0.21-bookworm-slim AS final
WORKDIR /app
COPY --from=build /app/publish .

# 非 root。用數字 UID（APP_UID=1654 由基底映像提供）：Kubernetes 的 runAsNonRoot 需要數字才能驗證。
# 監聽 8080（ASPNETCORE_HTTP_PORTS 由基底映像設定），>1024 所以非 root 可以綁定。
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "MedConnect.Api.dll"]
