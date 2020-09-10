# JsonToQueryable

Parse JSON string to expressions that used for querying in EntityFramework.Core 5.0

For example:
```js
{
	"query": {
		"Name": "$contains 'Default'"
	},
	"include": {
		"Children": {
			"query": {
				"Year": "= 2020"
			}
		},
		"AnotherChildren": {}
	},
	"type": "ParentType",
	"page": 1,
	"pageSize": 25
}
```
will be convert to
```csharp
dbContext.Parent.Include(p.Children.Where(c => c.Year == 2020))
                .Include(p.AnotherChildren)
                .Where(p=>p.Name.Contains("Default"))
                .Skip(page * pageSize)
                .Take(pageSize);
```

Support Where, Include, ThenInclude, Take, Skip

```csharp
var query = Context.CreateQuery(queryString, new List<Type>() { typeof(Parent) }).Cast<Parent>();
return query.ToList();
```
