# Steamworks Documentation parser

Simple parser for [the Steamworks documentation](https://partner.steamgames.com/home/steamworks). You can find the generated docs in the `docs` directory (it's HTML without most useless layout things and other unrelated stuff).

Generated docs aren't published here because of the NDA required to access the site. Everyone can sign in to [the partner site](https://partner.steamgames.com/), though - just try it with your Steam account!

# Why?

Because I'm getting tired of not knowing what has been changed in Steamworks documentation. No page history, nothing. Git can solve it. At least for me.

# Instructions

Copy `settings.json.default` to `settings.json` in your own build and set your Steam username and password.
It is possible to scrape the search page for any search queries you want (`searchQueries`, comma delimited). It will collect links to documentation pages (stops fetching if no useful links are found).
If you know about any hidden documentation which still isn't linked anywhere, add it to `predefinedDocs` (comma delimited).


Partner site will always require a Steam Guard code (because cookies always get removed when running with [Selenium WebDriver](http://docs.seleniumhq.org/projects/webdriver/)) and sometimes captcha, so don't be surprised.

## License

[WTFPL 2](http://www.wtfpl.net/).