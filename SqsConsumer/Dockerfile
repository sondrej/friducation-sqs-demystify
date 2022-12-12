FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["SqsConsumer/SqsConsumer.csproj", "SqsConsumer/"]
RUN dotnet restore "SqsConsumer/SqsConsumer.csproj"
COPY . .
WORKDIR "/src/SqsConsumer"
RUN dotnet build "SqsConsumer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SqsConsumer.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:7.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SqsConsumer.dll"]
