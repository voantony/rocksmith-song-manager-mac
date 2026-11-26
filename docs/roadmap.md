# Roadmap

The library reads manifest JSON from each package. Chart audio import is not implemented.

## Still out of scope

Official game files, including `songs.psarc`, are not modified. The scanner inflates the archive table and manifest entries only.

## Later: import audio and chart a custom song

Also not in v1. The app already has a seam for it, `ISongChartImporter` in `MacSongManager.Import`. The v1 implementation reports that import is unavailable. A future importer can be registered there without changing scanning or rename logic.

A likely pipeline:

1. Source separation, for example [Demucs](https://github.com/facebookresearch/demucs) (code is MIT; the repository was archived in 2025). Confirm the exact checkpoint before bundling. Some weights and the MUSDB training data have their own terms.
2. Pitch and note transcription, for example [Basic Pitch](https://github.com/spotify/basic-pitch) (Apache-2.0). Confirm the model file you ship is under that license and keep the copyright notice.
3. A beat and tempo map. Prefer a library with a clear license for application use (librosa is ISC). Review neural beat trackers individually; several are research-only or non-commercial.
4. An arrangement description derived from those notes.
5. A packaging step that produces a Mac custom song file (`_m.psarc`). That step is not implemented.

Only recordings the user has the rights to chart would be in scope. v1 has no network sync and does not download models.
