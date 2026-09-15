# 建置情境：倉庫根目錄
# docker build -f deploy/analysis/Dockerfile -t aiproject-analysis:local .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/AiProject.Analysis.Web/AiProject.Analysis.Web.csproj \
 && dotnet publish src/AiProject.Analysis.Web/AiProject.Analysis.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Hosting__Mode=SelfHosted
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AiProject.Analysis.Web.dll"]
