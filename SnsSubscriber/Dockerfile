FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["SnsSubscriber/SnsSubscriber.csproj", "SnsSubscriber/"]
RUN dotnet restore "SnsSubscriber/SnsSubscriber.csproj"
COPY . .
WORKDIR "/src/SnsSubscriber"
RUN dotnet build "SnsSubscriber.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SnsSubscriber.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:7.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SnsSubscriber.dll"]
