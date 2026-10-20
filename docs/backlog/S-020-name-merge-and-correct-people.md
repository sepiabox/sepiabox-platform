## S-020 — Name, merge and correct people
**Epic:** People
**Depends on:** S-019
**Priority:** High
**Size:** M

### Context
The model groups; only the family knows who is who. Manual corrections must never be undone by automatic assignment.

### Scope
- `/people/{id}`: rename (names unique case-insensitively), "This is me" ("Sou eu" in pt-BR; links the current user's e-mail; one per user), merge with another person, remove a face ("Not this person"; marks `excluded` for this person), mark "Not a face".
- Automatic assignment ignores excluded face/person pairs and `manual` faces.

### Out of scope
- Relationships between people (phase 3).

### Acceptance criteria
- [ ] Given two people merged, then the faces move to one and the counts add up.
- [ ] Given a face removed from a person, when I re-import, then it does not go back to that person.
- [ ] Given "This is me" by two users on the same person, then the second is rejected.
- [ ] Given a name already used by another person, then the change is rejected with a message.

### Technical notes
- Operations in a transaction; recompute centroids.

### Expected tests
- Integration (PostgreSQL): merge, exclude, uniqueness. bUnit: forms.
