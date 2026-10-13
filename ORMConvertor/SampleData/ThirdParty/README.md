# Third-party code

Excerpts of open-source projects whose data access the examples translate (decision 138). Each
folder is one project at one commit, with the project's licence beside the files:

| Folder | Project | Commit | Licence | Framework | Example |
|---|---|---|---|---|---|
| `keycloak/` | [keycloak/keycloak](https://github.com/keycloak/keycloak) | `5cbbdebe021e67ca834ed074dc59e13b3c224cfa` | Apache-2.0 | Hibernate | `keycloak-identity` |
| `ambari/` | [apache/ambari](https://github.com/apache/ambari) | `a01b6745cf618b20accde2e670b7c78115c21367` | Apache-2.0 | EclipseLink | `ambari-alerts` |
| `chinook/` | [cwoodruff/ChinookASPNETWebAPI](https://github.com/cwoodruff/ChinookASPNETWebAPI) | `82c89e0e5f6d650b6d6ec30e3499bf895ab4dad8` | MIT | Dapper, EF Core | `chinook-dapper`, `chinook-efcore` |
| `jpetstore/` | [mybatis/jpetstore-6](https://github.com/mybatis/jpetstore-6) | `344ef63ddf05dd83cd5bfc325a9e8ef7b143f860` | Apache-2.0 | MyBatis | `jpetstore-mybatis` |

- **Only permissive licences.** A project under MIT, Apache-2.0 or BSD can be taken; a project
  under a copyleft licence is catalogued, never copied here, so the repository's own MIT terms
  hold for everything outside this folder.
- **Every file says where it comes from.** It opens - after the project's own licence header,
  where it has one - with an `ORMConvertor:` note naming the project, the commit and the path,
  followed by `Unchanged apart from this note.` or by what was changed. A file the project does
  not have, such as the class that carries Keycloak's named queries, says so in its note.
- **A change keeps the meaning.** It rewrites what the tool reads only in another form, leaves
  out what the tool does not translate (statements that write, code that only serves them), or
  corrects an upstream mistake that keeps the code from running; the tables, the rows and the
  results stay those of the project. The `README.md` of each folder lists every change and why.
- **Nothing here is compiled.** `SampleData.csproj` removes the folder from compilation and
  embeds every file, licences included, in the assembly; `ThirdPartySources` reads them and
  `ORMConvertorAPI/Data/Examples.cs` composes the examples from them.

Which other projects were considered, what each would need and what the excerpts found in the
tool is `docs/analysis/real-world-sources.md` (in Czech, like the rest of `docs/`).
