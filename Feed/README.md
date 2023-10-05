# Bluesky feeds in C#

This repo is an example implementation of a feed generator for the [Bluesky](https://bsky.app) social network that hosts custom feeds. It is an implementation of the official example Typescript [feed generator](https://github.com/bluesky-social/feed-generator-example) in C#.

## How do feed generators work
When someone queries a feed generator they expect two things:
1. A list of posts
2. A cursor to the next page of posts

The post list is just a collection of links to posts. The cursor is a string that can be used to fetch the next page of posts. The cursor is opaque to the client and can be anything you want. It is up to the feed generator to encode the information needed to fetch the next page of posts into the cursor.

The sample implementation here works by listening to all new posts on bluesky, checking them for specific keywords based on the feeds we have configured and then storing them in a database. When a client requests a feed, the feed generator fetches the posts from the database that match the feed and returns them to the client. The cursor is just the id/timestamp of the last post returned.

## How does this project work
1. FirehoseListener listens for all events sent by Bluesky
2. For each event, it discards any events that aren't posts
3. For each post, it checks against the configured feeds, ie. LinuxFeed or FFXIVFeed which report whether the post should be included in the feed
4. If the post should be included, it is stored in the database
5. FeedGenerator listens for requests to the feed endpoint
6. For each request, it fetches the posts from the database that match the feed and returns them to the client

Using bitmasking, each post stored in the database can be associated with multiple feeds. This allows for a post to be included in multiple feeds without having to store multiple copies of the post.
This data structure may not be suited to something like returning specific posts based on each user requesting the feed, but it works well for the example.

## How to use this repo
The easiest way to get started is to clone this repo and build the docker image or deploy with docker-compose. 

You can build the docker image with the following commands: (or by simply specifying the `--build` flag when running `docker-compose up`)
```bash
docker build -t bluesky-feed-generator .
```

Rename `appsettings.example.json` to `appsettings.json` and fill in the values for your setup.

You can then run the image with the following command:
```bash
docker-compose up -d
```

*Note: This project does not register your feed generator with Bluesky. You can do this using the instructions on the [feed generator](https://github.com/bluesky-social/feed-generator-example) example.*