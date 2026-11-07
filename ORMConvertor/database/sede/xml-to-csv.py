#!/usr/bin/env python3
# Converts one table of a Stack Exchange data dump from its XML into the CSV load.sql bulk-loads
# (decision 208). Run by load-sede.sh once per line of dump.tsv:
#
#   xml-to-csv.py <Table.xml> <Table.csv> <Column,Column,...>
#
# The dump writes one <row .../> element per record with the columns as attributes and an
# absent attribute for NULL. The CSV gets the listed columns in their order - the order the
# staging table of load.sql declares -, an absent attribute as an empty field, every value as
# the attribute holds it (a date as yyyy-mm-ddThh:mm:ss.fff, a flag as True or False), quoted
# where it holds a comma, a quote or a line break. One change: the tags of a question in the
# spelling of SEDE, <tag><tag>, where the dump since 2024 writes |tag|tag|, so that the texts
# of the catalog, written against SEDE, read the data as they were written. The file is read
# as a stream, element by element, so a table of a gigabyte needs no memory to speak of.
import csv
import sys
import xml.etree.ElementTree as ET


def convert(xml_path, csv_path, columns):
    rows = 0
    with open(csv_path, "w", newline="", encoding="utf-8") as out:
        writer = csv.writer(out, quoting=csv.QUOTE_MINIMAL, lineterminator="\n")
        root = None
        for event, element in ET.iterparse(xml_path, events=("start", "end")):
            if event == "start":
                if root is None:
                    root = element
                continue
            if element.tag != "row":
                continue
            attributes = element.attrib
            row = []
            for column in columns:
                value = attributes.get(column)
                if value is None:
                    row.append("")
                elif column == "Tags" and value.startswith("|") and value.endswith("|"):
                    row.append("".join("<" + tag + ">" for tag in value.strip("|").split("|") if tag))
                else:
                    row.append(value)
            writer.writerow(row)
            rows += 1
            element.clear()
            if root is not None:
                root.clear()
    return rows


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit("usage: xml-to-csv.py <Table.xml> <Table.csv> <Column,Column,...>")
    count = convert(sys.argv[1], sys.argv[2], sys.argv[3].split(","))
    print(f"{sys.argv[1]}: {count} rows.")
