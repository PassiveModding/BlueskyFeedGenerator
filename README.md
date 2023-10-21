# Bluesky Feed Generator (.NET)
## Project Overview

## Bluesky.Feed
Bluesky.Feed is a custom feed .NET web server designed to serve posts from a database. 
It provides a robust and flexible platform for managing and delivering posts to clients. 

- Modify the `appsettings.json` file to configure the database connection string and other settings.
- The database schema is defined in the `Bluesky.Common` project.
- The `Bluesky.Feed` project contains the web server and API controllers.

## Bluesky.Firehose
Bluesky.Firehose is a .NET Core console application that can be used to seed the database with posts.

Due to how Bluesky works, all events that happen on the platform are published in real-time.

The Firehose application listens to these events and processes each as it arrives.
1. When a post event is received, it is run through multiple processing steps.
2. The post is then sanitized by removing punctuation, stop words, and other noise.
3. The sanitized text is then matched against keywords belonging to specific topics.
4. Each post is then assigned a score for each topic based on the number of keywords it matches.

The Firehose application is designed to run continuously and process events as they arrive.

## Bluesky.Firehose.Tests
Bluesky.Firehose.Tests is a .NET Core test project that contains unit tests for the Bluesky.Firehose project.
These tests cover the post sanitization and scoring logic.
You can run the tests using the `dotnet test` command.

## Bluesky.Common
Bluesky.Common contains shared code that is used by the other projects. This is where the database schema is defined.