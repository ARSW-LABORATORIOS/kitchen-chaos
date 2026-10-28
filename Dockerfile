FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY ["src/KitchenChaos.Server/KitchenChaos.Server.csproj", "src/KitchenChaos.Server/"]

RUN dotnet restore "src/KitchenChaos.Server/KitchenChaos.Server.csproj"

COPY . .

WORKDIR "/src/src/KitchenChaos.Server"

RUN dotnet publish "KitchenChaos.Server.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "KitchenChaos.Server.dll"]