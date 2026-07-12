#!/bin/bash
dotnet tool install -g dotnet-ef
export PATH="$PATH:/root/.dotnet/tools"
dotnet restore YTDLHub.sln
cd src/YTDLHub.Infrastructure
dotnet ef migrations add AddThumbnailUrl -s ../YTDLHub.Web/YTDLHub.Web.csproj
