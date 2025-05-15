# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["DockerLearningApi.csproj", "./"]
RUN dotnet restore

# Copy the rest of the code and build
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Copy EF Core tools to enable migrations
COPY --from=build /root/.dotnet/tools /root/.dotnet/tools
ENV PATH="/root/.dotnet/tools:${PATH}"

# Copy published app
COPY --from=build /app/publish .

# Make our migration script executable
COPY ["Scripts/wait-for-db.sh", "./"]
RUN chmod +x ./wait-for-db.sh

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Docker
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

# Set the entry point
ENTRYPOINT ["dotnet", "DockerLearningApi.dll"]