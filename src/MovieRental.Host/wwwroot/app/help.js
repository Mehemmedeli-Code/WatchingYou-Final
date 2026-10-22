function s(e,a=!1){return e.messages.map(t=>({id:t.id,sender:t.fromDesk===a?"user":"desk",content:t.body,authorName:t.authorName,at:t.createdAtUtc}))}export{s as t};
