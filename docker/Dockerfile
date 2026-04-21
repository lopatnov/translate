FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY translate.slnx .
COPY src/ src/
RUN dotnet restore src/Lopatnov.Translate.Grpc/Lopatnov.Translate.Grpc.csproj

COPY . .
RUN dotnet publish src/Lopatnov.Translate.Grpc/Lopatnov.Translate.Grpc.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=5100
EXPOSE 5100

ENTRYPOINT ["dotnet", "Lopatnov.Translate.Grpc.dll"]
