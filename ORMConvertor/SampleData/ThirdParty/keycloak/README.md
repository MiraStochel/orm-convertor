# Keycloak: users, roles, groups, clients

- **Project:** [keycloak/keycloak](https://github.com/keycloak/keycloak), an identity and access
  management server; JPA entities on Hibernate, `jakarta.persistence` annotations.
- **Commit:** `5cbbdebe021e67ca834ed074dc59e13b3c224cfa` (2026-10-13).
- **Licence:** Apache License 2.0, [`LICENSE.txt`](LICENSE.txt); the project has no NOTICE file.
- **Taken from:** `model/jpa/src/main/java/org/keycloak/models/jpa/entities/`, 21 of the 47
  entity classes there - the user, its attributes, credentials, federated identities, consents and
  required actions; roles with their attributes and composites; groups with their attributes and
  role mappings; clients and client scopes with their attributes and mappings.

## What was changed

1. **`KeycloakNamedQueries.java` is new.** Keycloak keeps its queries in `@NamedQuery`
   annotations on the entities and runs them with `em.createNamedQuery`; the tool reads a query
   where code hands it to the framework, not from the annotation. The class holds the 47 read-only
   named queries of these entities, each as a method that hands its text to `em.createQuery`.
   The annotations stay on the entities as they were; the 51 queries among them that delete,
   update or insert are not carried over.
2. **Twenty of the 47 are rewritten**, each with a note and its original text above it:
   - a comparison with a whole entity (`m.user = :user`) compares the key column with the key
     (`m.userId = :userId`) - the tool does not read an entity as a parameter;
   - a path through an association in a condition (`consent.user.id = :userId`) and a join whose
     one side is projected (`select r from RoleEntity r join CompositeRoleEntity c ...`,
     `select u from UserEntity u join u.attributes attr ...`) become a correlated `exists` - the
     tool reads neither a path outside `join` nor the projection of one side of a join;
   - `select distinct r ... join` becomes `exists` without `distinct`, which returns each role
     once as `distinct` did; the two queries over user attributes return a user with two equal
     attributes once rather than twice.
3. **One query is left out:** `getAllRedirectUrisOfEnabledClients` joins the element collection
   `client.redirectUris` and builds a `map`, neither of which the tool reads.
4. **Ten entities state their composite key differently.** Keycloak puts `@Id` on a `@ManyToOne`
   (JPA derived identity), for example `@Id @ManyToOne @JoinColumn(name = "USER_ID") UserEntity user`
   in `UserRoleMappingEntity`. Each such key part is written as its own `@Id @Column(name = "USER_ID")
   String userId` beside the same `@ManyToOne`, now `insertable = false, updatable = false`, and the
   nested `Key` class holds the column instead of the entity. The table, the key and the relation
   stay what they were. `FederatedIdentityEntity` already has a `userId` (the federated user's id),
   so its key part is called `localUserId`.

Everything else in the files - the remaining annotations, `@ElementCollection`, `@Nationalized`,
cascades, accessors, `equals` and `hashCode` - is the project's.
