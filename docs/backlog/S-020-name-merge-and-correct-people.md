## S-020 — Name, merge and correct people
**Epic:** People
**Depends on:** S-019
**Priority:** High
**Size:** M

### Context
The model groups; only the family knows who is who. Manual corrections must never be undone by automatic assignment.

### Scope
- `/people/{id}`: rename (names unique case-insensitively), "This is me" ("Sou eu" in pt-BR; links the current user's e-mail; one per user), merge with another person (after a confirmation that shows both), remove a face ("Not this person"; marks `excluded` for this person), mark "Not a face", give an excluded face back, and move a face to another person or a new one.
- Automatic assignment never changes a face the family touched: `manual` and `excluded` faces keep their person and choice on every re-import (D-077's carry-over), and an excluded face keeps pointing to the person it was removed from, so it never goes back to them; it can be given back ("Devolver") or moved ("Mover para…"). There is no table of excluded (face, person) pairs: moving the face covers the sibling case (D-079).

### Out of scope
- Relationships between people (phase 3).

### Acceptance criteria
- [x] Given two people merged, then the faces move to one and the counts add up. *(`PeopleCurationTests`: five faces from two people, one person with count 5, the centroid their normalized mean.)*
- [x] Given a face removed from a person, when I re-import, then it does not go back to that person. *(`PeopleCurationTests`: re-imported with a moved box and new numbers, the face stays excluded with that person; "Not a face" stays with nobody.)*
- [x] Given "This is me" by two users on the same person, then the second is rejected. *(`PeopleCurationTests`, and over HTTP in `PersonPagePostTests`.)*
- [x] Given a name already used by another person, then the change is rejected with a message. *(`PeopleCurationTests`, ignoring case; the page shows the message, `PersonPageFormTests`.)*

### Technical notes
- Operations in a transaction; recompute centroids.
- Decisions: D-018, D-077, D-079.

### Status
- `done` (2026-10-21): implemented and tested (D-079).

### Expected tests
- Integration (PostgreSQL): merge, exclude, uniqueness. bUnit: forms.
