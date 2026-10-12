# Node 6 prompt vs composer (recipe_head.json, current composer)

Composer output: `prompt_check/composed_now.json` (compose_portrait_prompt.py --recipe recipe_head.json). Node 6 = `TextEncodeQwenImage21` `inputs.prompt`.

## phase0: first workflow per seed
### seed 540101: DIFFERENT (node6 388 chars, composed 705 chars)
    --- composed_now
    +++ node6
    @@ -1,6 +1,4 @@
    -Edit image 1. Redraw the structural head shape and hair flow of her character from image 1 at exactly the same size and the same position, maintaining the exact base position: the same head shape and the same hairline and the same ink-brush painting medium.
    +Edit image 1. Keep her head, face and neck in image 1 exactly as shown: the same shape, the same position, the same size and the same ink-brush painting medium.
     Fill every other area of the image with the same flat solid magenta #FF00FF background.
    -Draw the face, ears and neck in soft, simple flat tones with minimal fine details to maintain the facial structure.
    -Exact dark jet-black hair colour as in image 1, a high contrast hair mass.
    -Every pixel below the neck alignment is flat solid magenta #FF00FF: clothing and torso elements are left as magenta.
    +The forehead, hairline, ears and neck are fully painted and visible, as one whole head.
     Only one character. No text, no logo, no watermark.

### seed 540102: DIFFERENT (node6 388 chars, composed 705 chars)
    --- composed_now
    +++ node6
    @@ -1,6 +1,4 @@
    -Edit image 1. Redraw the structural head shape and hair flow of her character from image 1 at exactly the same size and the same position, maintaining the exact base position: the same head shape and the same hairline and the same ink-brush painting medium.
    +Edit image 1. Keep her head, face and neck in image 1 exactly as shown: the same shape, the same position, the same size and the same ink-brush painting medium.
     Fill every other area of the image with the same flat solid magenta #FF00FF background.
    -Draw the face, ears and neck in soft, simple flat tones with minimal fine details to maintain the facial structure.
    -Exact dark jet-black hair colour as in image 1, a high contrast hair mass.
    -Every pixel below the neck alignment is flat solid magenta #FF00FF: clothing and torso elements are left as magenta.
    +The forehead, hairline, ears and neck are fully painted and visible, as one whole head.
     Only one character. No text, no logo, no watermark.

### seed 540103: DIFFERENT (node6 388 chars, composed 705 chars)
    --- composed_now
    +++ node6
    @@ -1,6 +1,4 @@
    -Edit image 1. Redraw the structural head shape and hair flow of her character from image 1 at exactly the same size and the same position, maintaining the exact base position: the same head shape and the same hairline and the same ink-brush painting medium.
    +Edit image 1. Keep her head, face and neck in image 1 exactly as shown: the same shape, the same position, the same size and the same ink-brush painting medium.
     Fill every other area of the image with the same flat solid magenta #FF00FF background.
    -Draw the face, ears and neck in soft, simple flat tones with minimal fine details to maintain the facial structure.
    -Exact dark jet-black hair colour as in image 1, a high contrast hair mass.
    -Every pixel below the neck alignment is flat solid magenta #FF00FF: clothing and torso elements are left as magenta.
    +The forehead, hairline, ears and neck are fully painted and visible, as one whole head.
     Only one character. No text, no logo, no watermark.

## v2: first workflow per seed
### seed 540101: DIFFERENT (node6 563 chars, composed 705 chars)
    --- composed_now
    +++ node6
    @@ -1,6 +1,5 @@
    -Edit image 1. Redraw the structural head shape and hair flow of her character from image 1 at exactly the same size and the same position, maintaining the exact base position: the same head shape and the same hairline and the same ink-brush painting medium.
    +Edit image 1. Redraw her face, neck and the hair that frames the face from image 1 as a larger close-up: the same face shape, the same expression and the same position and the same ink-brush painting medium.
     Fill every other area of the image with the same flat solid magenta #FF00FF background.
    -Draw the face, ears and neck in soft, simple flat tones with minimal fine details to maintain the facial structure.
    -Exact dark jet-black hair colour as in image 1, a high contrast hair mass.
    -Every pixel below the neck alignment is flat solid magenta #FF00FF: clothing and torso elements are left as magenta.
    +The forehead, hairline, ears and neck are fully painted and visible, as one whole head.
    +Every pixel below the neck is flat solid magenta #FF00FF: the body, shoulders, clothing, dress and sleeves are left as magenta.
     Only one character. No text, no logo, no watermark.

### seed 540102: DIFFERENT (node6 563 chars, composed 705 chars)
    --- composed_now
    +++ node6
    @@ -1,6 +1,5 @@
    -Edit image 1. Redraw the structural head shape and hair flow of her character from image 1 at exactly the same size and the same position, maintaining the exact base position: the same head shape and the same hairline and the same ink-brush painting medium.
    +Edit image 1. Redraw her face, neck and the hair that frames the face from image 1 as a larger close-up: the same face shape, the same expression and the same position and the same ink-brush painting medium.
     Fill every other area of the image with the same flat solid magenta #FF00FF background.
    -Draw the face, ears and neck in soft, simple flat tones with minimal fine details to maintain the facial structure.
    -Exact dark jet-black hair colour as in image 1, a high contrast hair mass.
    -Every pixel below the neck alignment is flat solid magenta #FF00FF: clothing and torso elements are left as magenta.
    +The forehead, hairline, ears and neck are fully painted and visible, as one whole head.
    +Every pixel below the neck is flat solid magenta #FF00FF: the body, shoulders, clothing, dress and sleeves are left as magenta.
     Only one character. No text, no logo, no watermark.

### seed 540103: DIFFERENT (node6 563 chars, composed 705 chars)
    --- composed_now
    +++ node6
    @@ -1,6 +1,5 @@
    -Edit image 1. Redraw the structural head shape and hair flow of her character from image 1 at exactly the same size and the same position, maintaining the exact base position: the same head shape and the same hairline and the same ink-brush painting medium.
    +Edit image 1. Redraw her face, neck and the hair that frames the face from image 1 as a larger close-up: the same face shape, the same expression and the same position and the same ink-brush painting medium.
     Fill every other area of the image with the same flat solid magenta #FF00FF background.
    -Draw the face, ears and neck in soft, simple flat tones with minimal fine details to maintain the facial structure.
    -Exact dark jet-black hair colour as in image 1, a high contrast hair mass.
    -Every pixel below the neck alignment is flat solid magenta #FF00FF: clothing and torso elements are left as magenta.
    +The forehead, hairline, ears and neck are fully painted and visible, as one whole head.
    +Every pixel below the neck is flat solid magenta #FF00FF: the body, shoulders, clothing, dress and sleeves are left as magenta.
     Only one character. No text, no logo, no watermark.

## v3: first workflow per seed
### seed 540101: DIFFERENT (node6 684 chars, composed 705 chars)
    --- composed_now
    +++ node6
    @@ -1,6 +1,6 @@
    -Edit image 1. Redraw the structural head shape and hair flow of her character from image 1 at exactly the same size and the same position, maintaining the exact base position: the same head shape and the same hairline and the same ink-brush painting medium.
    +Edit image 1. Redraw her head outline, hair mass and hair flow from image 1 at exactly the same size and the same position: the same head shape and the same hairline and the same ink-brush painting medium.
     Fill every other area of the image with the same flat solid magenta #FF00FF background.
    -Draw the face, ears and neck in soft, simple flat tones with minimal fine details to maintain the facial structure.
    -Exact dark jet-black hair colour as in image 1, a high contrast hair mass.
    -Every pixel below the neck alignment is flat solid magenta #FF00FF: clothing and torso elements are left as magenta.
    +The forehead, hairline, ears and neck are fully painted and visible, as one whole head.
    +Focus only on the hair mass, the structural head shape and the hair flow on a clean background, with minimal face details.
    +Every pixel below the neck is flat solid magenta #FF00FF: the body, shoulders, clothing, dress and sleeves are left as magenta.
     Only one character. No text, no logo, no watermark.

### seed 540102: DIFFERENT (node6 684 chars, composed 705 chars)
    --- composed_now
    +++ node6
    @@ -1,6 +1,6 @@
    -Edit image 1. Redraw the structural head shape and hair flow of her character from image 1 at exactly the same size and the same position, maintaining the exact base position: the same head shape and the same hairline and the same ink-brush painting medium.
    +Edit image 1. Redraw her head outline, hair mass and hair flow from image 1 at exactly the same size and the same position: the same head shape and the same hairline and the same ink-brush painting medium.
     Fill every other area of the image with the same flat solid magenta #FF00FF background.
    -Draw the face, ears and neck in soft, simple flat tones with minimal fine details to maintain the facial structure.
    -Exact dark jet-black hair colour as in image 1, a high contrast hair mass.
    -Every pixel below the neck alignment is flat solid magenta #FF00FF: clothing and torso elements are left as magenta.
    +The forehead, hairline, ears and neck are fully painted and visible, as one whole head.
    +Focus only on the hair mass, the structural head shape and the hair flow on a clean background, with minimal face details.
    +Every pixel below the neck is flat solid magenta #FF00FF: the body, shoulders, clothing, dress and sleeves are left as magenta.
     Only one character. No text, no logo, no watermark.

### seed 540103: DIFFERENT (node6 684 chars, composed 705 chars)
    --- composed_now
    +++ node6
    @@ -1,6 +1,6 @@
    -Edit image 1. Redraw the structural head shape and hair flow of her character from image 1 at exactly the same size and the same position, maintaining the exact base position: the same head shape and the same hairline and the same ink-brush painting medium.
    +Edit image 1. Redraw her head outline, hair mass and hair flow from image 1 at exactly the same size and the same position: the same head shape and the same hairline and the same ink-brush painting medium.
     Fill every other area of the image with the same flat solid magenta #FF00FF background.
    -Draw the face, ears and neck in soft, simple flat tones with minimal fine details to maintain the facial structure.
    -Exact dark jet-black hair colour as in image 1, a high contrast hair mass.
    -Every pixel below the neck alignment is flat solid magenta #FF00FF: clothing and torso elements are left as magenta.
    +The forehead, hairline, ears and neck are fully painted and visible, as one whole head.
    +Focus only on the hair mass, the structural head shape and the hair flow on a clean background, with minimal face details.
    +Every pixel below the neck is flat solid magenta #FF00FF: the body, shoulders, clothing, dress and sleeves are left as magenta.
     Only one character. No text, no logo, no watermark.

## v4: first workflow per seed
### seed 540101: IDENTICAL (node6 705 chars, composed 705 chars)

### seed 540102: IDENTICAL (node6 705 chars, composed 705 chars)

### seed 540103: IDENTICAL (node6 705 chars, composed 705 chars)

