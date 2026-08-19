// Mirrors Model.ConversionContentType. The values name a language, not a framework
// (decision 025), so SQL and HQL sit beside the C# and XML ones.
export enum ContentType {
  CSharpEntity = 10,
  CSharpQuery = 20,
  XML = 30,
  SqlQuery = 40,
  HqlQuery = 50,
}
