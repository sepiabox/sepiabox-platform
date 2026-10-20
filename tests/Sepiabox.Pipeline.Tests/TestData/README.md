# Test data

Where committed fixtures come from, when they were not generated for the tests.

| File | What it is | Source and license |
|---|---|---|
| `face-portrait.jpg` | One face, for face detection and embeddings (S-018). 400×400 JPEG, quality 85, metadata removed. | Official NASA portrait of astronaut Eileen Collins, a work of the US federal government and therefore in the **public domain**. Taken from scikit-image's `skimage/data/astronaut.png` at tag v0.24.0 (commit `a160f384523ac70173d069954b7084bee79fb68e`; SHA-256 of the PNG `88431cd9653ccd539741b555fb0a46b61558b301d4110412b5bc28b5e3ea6cb5`), whose `skimage.data.astronaut` docs say it "was downloaded from the NASA Great Images database" and has "no known copyright restrictions, released into the public domain". Wikimedia Commons was not reachable from the development sandbox, so the copy came through scikit-image. Resized from 512×512 with ImageMagick: `convert astronaut.png -strip -resize 400x400 -quality 85 face-portrait.jpg`. |
