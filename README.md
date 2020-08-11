# FrequencyWords
Repository for Frequency Word List Generator and processed files

In early days I hosted the generated files on OneDrive with my blog https://invokeit.wordpress.com/frequency-word-lists/ linking to it.
Moving forward, the code and the generated outputs are on GitHub.

### OpenSubtitle tokenized source
The data used to generate 2016 lists can be found at http://opus.lingfil.uu.se/OpenSubtitles2016.php 
The data used to generate 2018 lists can be found at http://opus.nlpl.eu/OpenSubtitles2018.php

### Format
Frequency lists are on the `{word}{space}{numer_of_occurences_in_corpus}`. By example, in file `en_50k.txt` :
```
you 22484400
i 19975318
the 17594291
to 13200962
...
```

### Usages
These data are reused by various widely used opensource projects, among which Wikipedia, input methods and autocomplete keyoards, etc.

### License 
MIT License for code.<br>
CC-by-sa-4.0 for content.
