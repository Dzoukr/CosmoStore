module CosmoStore.CosmosDb.Tests.V2.Data
open Newtonsoft.Json.Linq

let json = 
    """
[
  {
    "_id": "5c7e1625764b7ea8ccffbf92",
    "index": 0,
    "guid": "4c3cfb6e-7c9e-414f-88d6-0422a7140895",
    "isActive": true,
    "balance": "$3,392.52",
    "picture": "http://placehold.it/32x32",
    "age": 35,
    "eyeColor": "brown",
    "name": {
      "first": "Barrera",
      "last": "Carlson"
    },
    "company": "PYRAMIA",
    "email": "barrera.carlson@pyramia.biz",
    "phone": "+1 (949) 401-3562",
    "address": "265 Sumner Place, Homeland, Florida, 7936",
    "about": "Sint aliquip ut anim est. Occaecat anim sunt dolor sint laborum do sit. Ex do culpa elit amet velit reprehenderit duis culpa ad eiusmod nisi ut Lorem aute. Cupidatat minim cupidatat excepteur mollit quis qui sint incididunt enim reprehenderit do excepteur.",
    "registered": "Thursday, April 2, 2015 6:22 PM",
    "latitude": "10.925641",
    "longitude": "-156.645804",
    "tags": [
      "ad",
      "irure",
      "ex",
      "sunt",
      "voluptate"
    ],
    "range": [
      0,
      1,
      2,
      3,
      4,
      5,
      6,
      7,
      8,
      9
    ],
    "friends": [
      {
        "id": 0,
        "name": "Jayne Mercer"
      },
      {
        "id": 1,
        "name": "Parsons Ortiz"
      },
      {
        "id": 2,
        "name": "Wheeler Johns"
      }
    ],
    "greeting": "Hello, Barrera! You have 7 unread messages.",
    "favoriteFruit": "strawberry"
  },
  {
    "_id": "5c7e162537e86f231909f6e6",
    "index": 1,
    "guid": "f2484fa8-c0b8-4722-a68d-2780bd805ced",
    "isActive": false,
    "balance": "$3,804.93",
    "picture": "http://placehold.it/32x32",
    "age": 30,
    "eyeColor": "green",
    "name": {
      "first": "Tania",
      "last": "Horne"
    },
    "company": "HOTCAKES",
    "email": "tania.horne@hotcakes.us",
    "phone": "+1 (953) 477-3604",
    "address": "776 Gold Street, Williston, Missouri, 8007",
    "about": "Tempor consectetur in commodo elit laborum ea. Pariatur et amet aliqua velit velit irure velit consectetur ea pariatur ullamco. Velit veniam aliqua consequat aliquip do deserunt fugiat.",
    "registered": "Wednesday, January 28, 2015 9:52 AM",
    "latitude": "-77.484018",
    "longitude": "152.341916",
    "tags": [
      "deserunt",
      "exercitation",
      "elit",
      "minim",
      "consequat"
    ],
    "range": [
      0,
      1,
      2,
      3,
      4,
      5,
      6,
      7,
      8,
      9
    ],
    "friends": [
      {
        "id": 0,
        "name": "Brianna Santana"
      },
      {
        "id": 1,
        "name": "Cherry Dickerson"
      },
      {
        "id": 2,
        "name": "Austin Baldwin"
      }
    ],
    "greeting": "Hello, Tania! You have 10 unread messages.",
    "favoriteFruit": "strawberry"
  },
  {
    "_id": "5c7e1625907425952dbcb82f",
    "index": 2,
    "guid": "cf9dacf4-7cf0-408f-9651-7888683a3eb2",
    "isActive": false,
    "balance": "$3,793.80",
    "picture": "http://placehold.it/32x32",
    "age": 33,
    "eyeColor": "blue",
    "name": {
      "first": "Jocelyn",
      "last": "Gates"
    },
    "company": "ZOUNDS",
    "email": "jocelyn.gates@zounds.net",
    "phone": "+1 (942) 533-3180",
    "address": "807 Brown Street, Dola, Connecticut, 4041",
    "about": "Eu ullamco ipsum cupidatat proident ex. Irure quis velit tempor aliquip exercitation est incididunt occaecat quis laborum aute. Sint Lorem cupidatat amet nulla nisi elit non occaecat sit. Aliqua reprehenderit occaecat aliqua magna officia. Non Lorem in id velit ipsum labore enim cillum et Lorem consequat. Ex ea ea officia ex incididunt reprehenderit ullamco in adipisicing voluptate enim in officia nostrud. Qui excepteur laboris non dolor excepteur nostrud Lorem incididunt ex voluptate.",
    "registered": "Saturday, October 15, 2016 4:29 AM",
    "latitude": "19.760841",
    "longitude": "-170.32908",
    "tags": [
      "magna",
      "est",
      "officia",
      "incididunt",
      "fugiat"
    ],
    "range": [
      0,
      1,
      2,
      3,
      4,
      5,
      6,
      7,
      8,
      9
    ],
    "friends": [
      {
        "id": 0,
        "name": "Kari Whitehead"
      },
      {
        "id": 1,
        "name": "Irma Gonzalez"
      },
      {
        "id": 2,
        "name": "Nicholson Reynolds"
      }
    ],
    "greeting": "Hello, Jocelyn! You have 10 unread messages.",
    "favoriteFruit": "banana"
  },
  {
    "_id": "5c7e162547463b785ec5b63e",
    "index": 3,
    "guid": "93501a60-a24c-4c0d-a05b-3b4d5458e5c9",
    "isActive": true,
    "balance": "$1,383.59",
    "picture": "http://placehold.it/32x32",
    "age": 28,
    "eyeColor": "blue",
    "name": {
      "first": "Luna",
      "last": "Fischer"
    },
    "company": "ZERBINA",
    "email": "luna.fischer@zerbina.me",
    "phone": "+1 (931) 459-3880",
    "address": "907 Hutchinson Court, Coinjock, New Hampshire, 5199",
    "about": "Do adipisicing occaecat et nulla est culpa anim ut ad veniam. Id irure veniam voluptate deserunt consectetur. Cupidatat exercitation cillum proident quis laboris ipsum cupidatat anim esse eu. Sit culpa dolor magna nostrud aliquip. Sunt ex deserunt non ex culpa occaecat quis velit. Dolor laborum ipsum deserunt quis in id. Duis duis anim ea cillum ex.",
    "registered": "Sunday, September 18, 2016 1:58 AM",
    "latitude": "-86.58885",
    "longitude": "-100.271988",
    "tags": [
      "duis",
      "reprehenderit",
      "nulla",
      "aliqua",
      "exercitation"
    ],
    "range": [
      0,
      1,
      2,
      3,
      4,
      5,
      6,
      7,
      8,
      9
    ],
    "friends": [
      {
        "id": 0,
        "name": "Delacruz Spears"
      },
      {
        "id": 1,
        "name": "Maldonado Ellison"
      },
      {
        "id": 2,
        "name": "Dixie Hooper"
      }
    ],
    "greeting": "Hello, Luna! You have 8 unread messages.",
    "favoriteFruit": "banana"
  },
  {
    "_id": "5c7e1625b9dadccaea48fca6",
    "index": 4,
    "guid": "6fe3d6b8-dcf3-40ad-b603-af500aeb23d2",
    "isActive": true,
    "balance": "$3,671.04",
    "picture": "http://placehold.it/32x32",
    "age": 30,
    "eyeColor": "blue",
    "name": {
      "first": "Gordon",
      "last": "Jackson"
    },
    "company": "GEEKKO",
    "email": "gordon.jackson@geekko.org",
    "phone": "+1 (950) 471-2833",
    "address": "460 Hendrickson Street, Robinette, North Carolina, 3297",
    "about": "Lorem aute laboris sit nostrud eu est amet eiusmod anim labore est. Exercitation esse pariatur cupidatat esse amet labore laboris eu dolor do aute sunt. Laborum duis officia occaecat qui consectetur pariatur et exercitation occaecat ut Lorem velit magna magna.",
    "registered": "Friday, November 24, 2017 5:02 AM",
    "latitude": "-88.731989",
    "longitude": "52.694706",
    "tags": [
      "proident",
      "ex",
      "ea",
      "commodo",
      "do"
    ],
    "range": [
      0,
      1,
      2,
      3,
      4,
      5,
      6,
      7,
      8,
      9
    ],
    "friends": [
      {
        "id": 0,
        "name": "Velazquez Tucker"
      },
      {
        "id": 1,
        "name": "Nannie Colon"
      },
      {
        "id": 2,
        "name": "Susana Kane"
      }
    ],
    "greeting": "Hello, Gordon! You have 9 unread messages.",
    "favoriteFruit": "strawberry"
  },
  {
    "_id": "5c7e1625de724bc08e084ea6",
    "index": 5,
    "guid": "75a26106-2a40-44a7-9ef0-21ef6cdbf7f4",
    "isActive": true,
    "balance": "$1,748.09",
    "picture": "http://placehold.it/32x32",
    "age": 21,
    "eyeColor": "blue",
    "name": {
      "first": "Valenzuela",
      "last": "Odom"
    },
    "company": "EZENT",
    "email": "valenzuela.odom@ezent.com",
    "phone": "+1 (881) 437-2211",
    "address": "247 Hewes Street, Sultana, South Dakota, 2453",
    "about": "Mollit et consectetur aliqua amet dolor. Qui reprehenderit cillum ipsum aute commodo. Amet qui dolor eu ut. Lorem ea proident labore in. Nisi cillum occaecat adipisicing laborum.",
    "registered": "Wednesday, April 5, 2017 12:49 PM",
    "latitude": "-28.508634",
    "longitude": "53.733055",
    "tags": [
      "adipisicing",
      "aliquip",
      "ex",
      "amet",
      "deserunt"
    ],
    "range": [
      0,
      1,
      2,
      3,
      4,
      5,
      6,
      7,
      8,
      9
    ],
    "friends": [
      {
        "id": 0,
        "name": "Mosley Carson"
      },
      {
        "id": 1,
        "name": "Hood Mueller"
      },
      {
        "id": 2,
        "name": "Ester Hutchinson"
      }
    ],
    "greeting": "Hello, Valenzuela! You have 10 unread messages.",
    "favoriteFruit": "banana"
  },
  {
    "_id": "5c7e1625ad69789b9cd14a7c",
    "index": 6,
    "guid": "88e35c74-3758-40f4-9c54-e86c201b8cd6",
    "isActive": false,
    "balance": "$2,293.69",
    "picture": "http://placehold.it/32x32",
    "age": 38,
    "eyeColor": "brown",
    "name": {
      "first": "Sabrina",
      "last": "Lancaster"
    },
    "company": "SNIPS",
    "email": "sabrina.lancaster@snips.ca",
    "phone": "+1 (904) 464-2698",
    "address": "500 Lawton Street, Caberfae, New York, 1579",
    "about": "Nostrud dolor aliquip id veniam nostrud magna amet exercitation eiusmod. Eiusmod nostrud esse commodo ipsum reprehenderit et labore proident sint culpa do veniam. Incididunt incididunt sunt fugiat labore irure amet incididunt. Eu laborum consectetur sunt qui occaecat. Commodo officia mollit ea adipisicing duis elit consectetur velit duis mollit. Voluptate et aliquip veniam cupidatat tempor labore nostrud ut do sint. Aliqua adipisicing dolor qui fugiat id id aute voluptate laborum ex mollit occaecat.",
    "registered": "Tuesday, March 7, 2017 10:03 AM",
    "latitude": "-80.68699",
    "longitude": "-146.070976",
    "tags": [
      "irure",
      "anim",
      "nostrud",
      "nisi",
      "et"
    ],
    "range": [
      0,
      1,
      2,
      3,
      4,
      5,
      6,
      7,
      8,
      9
    ],
    "friends": [
      {
        "id": 0,
        "name": "Webb Prince"
      },
      {
        "id": 1,
        "name": "Frances Koch"
      },
      {
        "id": 2,
        "name": "Victoria Hansen"
      }
    ],
    "greeting": "Hello, Sabrina! You have 7 unread messages.",
    "favoriteFruit": "banana"
  },
  {
    "_id": "5c7e16257521dbbf3e4a7fca",
    "index": 7,
    "guid": "40fe71a8-1b01-4b4c-9f68-1f1586f77f5a",
    "isActive": false,
    "balance": "$2,078.09",
    "picture": "http://placehold.it/32x32",
    "age": 30,
    "eyeColor": "green",
    "name": {
      "first": "Price",
      "last": "Miles"
    },
    "company": "GEEKWAGON",
    "email": "price.miles@geekwagon.biz",
    "phone": "+1 (813) 558-2525",
    "address": "445 Knickerbocker Avenue, Ferney, Illinois, 1466",
    "about": "Mollit adipisicing voluptate aliquip do reprehenderit tempor excepteur ea adipisicing nulla esse tempor sit. Eu aliquip sint ipsum cillum reprehenderit. Culpa exercitation nisi dolore voluptate ea eu. Irure nulla veniam aliqua quis id consequat non labore minim.",
    "registered": "Wednesday, October 28, 2015 8:40 AM",
    "latitude": "-32.083219",
    "longitude": "-127.696443",
    "tags": [
      "consequat",
      "occaecat",
      "non",
      "laborum",
      "eiusmod"
    ],
    "range": [
      0,
      1,
      2,
      3,
      4,
      5,
      6,
      7,
      8,
      9
    ],
    "friends": [
      {
        "id": 0,
        "name": "Mccarthy Saunders"
      },
      {
        "id": 1,
        "name": "Evelyn Stein"
      },
      {
        "id": 2,
        "name": "Medina Downs"
      }
    ],
    "greeting": "Hello, Price! You have 7 unread messages.",
    "favoriteFruit": "apple"
  }
]
""" 
    |> JToken.Parse