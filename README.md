[![Build status](https://ci.appveyor.com/api/projects/status/a0qpxxhfs7uagk9x/branch/master?svg=true)](https://ci.appveyor.com/project/nickbabcock/pfarah/branch/master)
[![Build status](https://travis-ci.org/nickbabcock/Pfarah.svg?branch=master)](https://travis-ci.org/nickbabcock/Pfarah)
[![Coverage Status](https://coveralls.io/repos/nickbabcock/Pfarah/badge.svg)](https://coveralls.io/r/nickbabcock/Pfarah)

# Pfarah

Parses files generated by the Clausewitz engine (Paradox Interactive) into
a DOM structure that can be queried upon

[Documentation](https://nickbabcock.github.io/Pfarah/)

## Why Pfarah?

Pfarah was initially created because Paradox save files like EU4, change
subtlety with each version and it is too much work to version the API because
the API consists of fifteen hundred properties and a savegame doesn't need to
have all the properties so it can be near impossible to know what changed. A
DOM parser like Pfarah is flexible enough to fit the bill.

## Contributing

Since this isn't a large enough project to have its own mailing list, if you
want to start a discussion, open an issue.

Contributions are welcomed as well! Fork and create a pull request as needed.

## Design Decisions

The backing store of `ParaValue.Array` is an `array` because the native F#
`list` is too slow and C#'s `List` (F#'s `ResizeArray`) is cumbersome to work
with in F#, not to mention using a `List` would mean wasted space because of
unused slots in underlying array. Thus a balance was struck by using the
native array as the interface. It's fast and easy to work with in F#.

The same argument is used in part to explain the backing store of
`ParaValue.Record`. It may be tempting to an associative data structure,
mapping strings to ParaValues, but there are several drawbacks to this
approach. First F#'s map, through no fault of its own, will always be slower
than its mutable brother the `Dictionary`. However, a dictionary is hard to
work with in F#. Not to mention, with the data format, one has no idea how
many times a key is going to occur, and so the dictionary must be constantly
updated. If there are no elements of a parsed key in the dictionary, add a
single element. Else if a single element already exists, replace it with a two
element array. Else append the element to the existing array. The process is
definitely tedious and doesn't add any value. This problem is further
exasperated if similar objects don't have an instance of a key or contain only
one -- this would then constitute three different interfaces for these values
(zero value, one value, more than one value).

Even though the data format guarantees that the file is strictly a `Record`,
the return value of `Parse` and `Load` returns a `ParaValue` because it allows
for a consistent API. The dynamic operator operates the same on the root
element as the nth nested element.

Maybe the best question to answer is: why F#?

- F# is fast: creating a DOM structure at 65MB/s
- F# is flexible: Pfarah uses a mix of functional and imperative code
- F# has a good package manager (nuget, paket)
- F# is readable: F# doesn't contain many exotic symbols
- F# is simple: Hardly any new concepts need to be learned to grok the code
- F# is mature: This library should be able to take any use and abuse from here on out on any platform that F# runs on
