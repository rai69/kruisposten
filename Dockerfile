FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Triodos.KruispostMonitor.slnx .
COPY src/Triodos.KruispostMonitor/Triodos.KruispostMonitor.csproj src/Triodos.KruispostMonitor/
COPY tests/Triodos.KruispostMonitor.Tests/Triodos.KruispostMonitor.Tests.csproj tests/Triodos.KruispostMonitor.Tests/
RUN dotnet restore

COPY . .
RUN dotnet test --no-restore
RUN dotnet publish src/Triodos.KruispostMonitor -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Triodos.KruispostMonitor.dll"]
