import { one } from "./lib.mjs";
const x = one("SELECT TOP 1 CAST(NameRu AS varbinary(200)) AS bin FROM cinema.TicketTypes WHERE NameRu LIKE N'APITEST%'");
const name = Buffer.from(x.bin, "base64").toString("utf16le");
console.log(name, name === "APITEST Тариф");
