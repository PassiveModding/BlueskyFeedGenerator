# Keyword file example

Create a file in the `Firehose/keywords` directory with the name of your topic. The file should contain a list of keywords, one per line, along with the weight of the keyword. The weight is a number between 0 and 100, where 100 is the most important keyword. The weight is used to determine which feed a post belongs to if it matches multiple keywords.
For example, `Firehose/keywords/science.csv` might contain the following:

avoid adding when the keyword is a subset of another matched keyword
ie. warrior is a subset of warrior|light
we should also avoid adding when the keyword is a superset of another matched keyword
ie. warrior|light is a superset of warrior

```
science,100
biology,100
chemistry,100
physics,100
quantum entanglement,100 # words must be contained in the post in the same order
ocean|discover,100 # both words must be contained in the post anywhere
ocean,30 # single word match, less important than ocean|discover and will be discarded if both match
quantum,30
```

Using above example let's analyze a few posts:

Post: "Scientists discover new species of fish in the ocean"
After normalization, removal of short words and lemmitization: "scientist discover new specie fish ocean"
Matches: science +100, ocean +30
Score: 130

Post: "A new ocean discovered on Mars"
After normalization, removal of short words and lemmitization: "new ocean discover mars"
Matches: ocean +30 but since it contains both ocean and discover, it matches ocean|discover +100 and the initial ocean +30 is ignored.
Score: 100

Post: "Quantum entanglement experiment"
After normalization, removal of short words and lemmitization: "quantum entanglement experiment"
Matches: quantum entanglement +100, quantum +30, since quantum entanglement is a more specific match, it is used over quantum.
Score: 100

