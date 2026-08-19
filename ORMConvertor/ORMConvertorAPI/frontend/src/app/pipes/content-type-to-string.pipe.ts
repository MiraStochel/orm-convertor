import { Pipe, PipeTransform } from "@angular/core";
import { ContentType } from "../model/content-type";

@Pipe({
  name: "contentTypeToString",
})
export class ContentTypeToStringPipe implements PipeTransform {
  transform(value: ContentType): string {
    switch (value) {
      case ContentType.CSharpEntity:
        return "Entity Class";
      case ContentType.XML:
        return "XML Mapping";
      case ContentType.CSharpQuery:
        return "Query (LINQ)";
      case ContentType.SqlQuery:
        return "Query (SQL)";
      case ContentType.HqlQuery:
        return "Query (HQL)";
      default:
        return "Unknown";
    }
  }
}
