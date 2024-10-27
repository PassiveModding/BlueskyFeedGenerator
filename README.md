# Bluesky Feed Generator (.NET)
## Project Overview

Feed generators are a way to create custom feeds for the Bluesky platform. 
This project is a .NET implementation of a feed generator.

This project is broken up into two main components:
- **Classifiers**: These are the classes that are used to classify and store data from the bluesky firehose (JetStream)
- **Feed Generators**: These are the classes that are used to filter and handle serving posts in a custom feed.

## Classifiers
Classifiers are used to classify and store data from the bluesky firehose.
Right now, the classifiers provided are:
- **HelloClassifier**: This classifier is used to classify posts that contain the word "hello" in them.
- **LikeClassifier**: This classifier is used to capture all likes on posts.

Classifiers implement a `Cleanup` method that is used to clean up any data that is no longer needed. 
These classifiers currently cleanup likes older than 1 day and hello posts older than 7 days.

## Feed Generators
Feed generators are used to filter and handle serving posts in a custom feed.

### HelloFeedProvider
The hello feed provider serves posts that have been classified by the HelloClassifier.

### LikedByFollowersProvider and LikedByFollowingProvider
The like feeds perform a lookup of the user requesting the feed before filtering likes served by LikeClassifier.
- **LikedByFollowersProvider**: This feed provider serves posts that have been liked by users that follow the user requesting the feed.
- **LikedByFollowingProvider**: This feed provider serves posts that have been liked by users that the user requesting the feed follows.