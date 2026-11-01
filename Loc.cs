namespace IpGuard;

/// <summary>Russian names for the countries and cities the widget can meet.</summary>
public static class Loc
{
    private static readonly Dictionary<string, string> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RU"] = "Россия", ["BY"] = "Беларусь", ["UA"] = "Украина", ["KZ"] = "Казахстан",
        ["NL"] = "Нидерланды", ["DE"] = "Германия", ["FR"] = "Франция", ["IT"] = "Италия",
        ["ES"] = "Испания", ["PT"] = "Португалия", ["GB"] = "Великобритания", ["IE"] = "Ирландия",
        ["US"] = "США", ["CA"] = "Канада", ["MX"] = "Мексика", ["BR"] = "Бразилия",
        ["AR"] = "Аргентина", ["CL"] = "Чили", ["CO"] = "Колумбия", ["PE"] = "Перу",
        ["SE"] = "Швеция", ["NO"] = "Норвегия", ["DK"] = "Дания", ["FI"] = "Финляндия",
        ["IS"] = "Исландия", ["PL"] = "Польша", ["CZ"] = "Чехия", ["SK"] = "Словакия",
        ["HU"] = "Венгрия", ["RO"] = "Румыния", ["BG"] = "Болгария", ["GR"] = "Греция",
        ["TR"] = "Турция", ["RS"] = "Сербия", ["HR"] = "Хорватия", ["SI"] = "Словения",
        ["BA"] = "Босния и Герцеговина", ["MK"] = "Северная Македония", ["AL"] = "Албания",
        ["ME"] = "Черногория", ["CH"] = "Швейцария", ["AT"] = "Австрия", ["BE"] = "Бельгия",
        ["LU"] = "Люксембург", ["LV"] = "Латвия", ["LT"] = "Литва", ["EE"] = "Эстония",
        ["MD"] = "Молдова", ["GE"] = "Грузия", ["AM"] = "Армения", ["AZ"] = "Азербайджан",
        ["UZ"] = "Узбекистан", ["KG"] = "Кыргызстан", ["TJ"] = "Таджикистан", ["TM"] = "Туркменистан",
        ["JP"] = "Япония", ["KR"] = "Южная Корея", ["CN"] = "Китай", ["HK"] = "Гонконг",
        ["TW"] = "Тайвань", ["SG"] = "Сингапур", ["TH"] = "Таиланд", ["VN"] = "Вьетнам",
        ["MY"] = "Малайзия", ["ID"] = "Индонезия", ["PH"] = "Филиппины", ["IN"] = "Индия",
        ["PK"] = "Пакистан", ["AU"] = "Австралия", ["NZ"] = "Новая Зеландия", ["ZA"] = "ЮАР",
        ["AE"] = "ОАЭ", ["SA"] = "Саудовская Аравия", ["IL"] = "Израиль", ["EG"] = "Египет",
        ["MA"] = "Марокко", ["TN"] = "Тунис", ["DZ"] = "Алжир", ["NG"] = "Нигерия",
        ["KE"] = "Кения", ["CY"] = "Кипр", ["MT"] = "Мальта", ["QA"] = "Катар",
        ["KW"] = "Кувейт", ["OM"] = "Оман", ["BH"] = "Бахрейн", ["JO"] = "Иордания",
        ["LB"] = "Ливан", ["CR"] = "Коста-Рика", ["PA"] = "Панама",
        ["UY"] = "Уругвай", ["PY"] = "Парагвай", ["BO"] = "Боливия", ["EC"] = "Эквадор",
        ["VE"] = "Венесуэла", ["CU"] = "Куба", ["DO"] = "Доминикана", ["GT"] = "Гватемала",
        ["SV"] = "Сальвадор", ["HN"] = "Гондурас", ["NI"] = "Никарагуа", ["BD"] = "Бангладеш",
        ["LK"] = "Шри-Ланка", ["NP"] = "Непал", ["KH"] = "Камбоджа", ["LA"] = "Лаос",
        ["MM"] = "Мьянма", ["MN"] = "Монголия", ["BN"] = "Бруней", ["MO"] = "Макао",
        ["PR"] = "Пуэрто-Рико", ["BM"] = "Бермуды", ["GI"] = "Гибралтар", ["IM"] = "Остров Мэн",
        ["JE"] = "Джерси", ["GG"] = "Гернси", ["FO"] = "Фарерские острова", ["GL"] = "Гренландия",
        ["LI"] = "Лихтенштейн", ["MC"] = "Монако", ["SM"] = "Сан-Марино", ["AD"] = "Андорра",
        ["VA"] = "Ватикан", ["XK"] = "Косово"
    };

    private static readonly Dictionary<string, string> Cities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Moscow"] = "Москва",
        ["Saint Petersburg"] = "Санкт-Петербург",
        ["St Petersburg"] = "Санкт-Петербург",
        ["Novosibirsk"] = "Новосибирск",
        ["Yekaterinburg"] = "Екатеринбург",
        ["Kazan"] = "Казань",
        ["Amsterdam"] = "Амстердам",
        ["Rotterdam"] = "Роттердам",
        ["The Hague"] = "Гаага",
        ["Berlin"] = "Берлин",
        ["Munich"] = "Мюнхен",
        ["Hamburg"] = "Гамбург",
        ["Frankfurt am Main"] = "Франкфурт-на-Майне",
        ["Frankfurt"] = "Франкфурт",
        ["Cologne"] = "Кёльн",
        ["Düsseldorf"] = "Дюссельдорф",
        ["Dusseldorf"] = "Дюссельдорф",
        ["Stuttgart"] = "Штутгарт",
        ["Paris"] = "Париж",
        ["Marseille"] = "Марсель",
        ["Lyon"] = "Лион",
        ["London"] = "Лондон",
        ["Manchester"] = "Манчестер",
        ["Dublin"] = "Дублин",
        ["Warsaw"] = "Варшава",
        ["Krakow"] = "Краков",
        ["Prague"] = "Прага",
        ["Vienna"] = "Вена",
        ["Zurich"] = "Цюрих",
        ["Geneva"] = "Женева",
        ["Milan"] = "Милан",
        ["Rome"] = "Рим",
        ["Madrid"] = "Мадрид",
        ["Barcelona"] = "Барселона",
        ["Lisbon"] = "Лиссабон",
        ["Copenhagen"] = "Копенгаген",
        ["Stockholm"] = "Стокгольм",
        ["Oslo"] = "Осло",
        ["Helsinki"] = "Хельсинки",
        ["Reykjavik"] = "Рейкьявик",
        ["Tallinn"] = "Таллин",
        ["Riga"] = "Рига",
        ["Vilnius"] = "Вильнюс",
        ["Kyiv"] = "Киев",
        ["Kiev"] = "Киев",
        ["Minsk"] = "Минск",
        ["Almaty"] = "Алматы",
        ["Astana"] = "Астана",
        ["Tbilisi"] = "Тбилиси",
        ["Yerevan"] = "Ереван",
        ["Baku"] = "Баку",
        ["Tashkent"] = "Ташкент",
        ["Bishkek"] = "Бишкек",
        ["Chisinau"] = "Кишинёв",
        ["Istanbul"] = "Стамбул",
        ["Ankara"] = "Анкара",
        ["Dubai"] = "Дубай",
        ["Tel Aviv"] = "Тель-Авив",
        ["Tokyo"] = "Токио",
        ["Seoul"] = "Сеул",
        ["Singapore"] = "Сингапур",
        ["Hong Kong"] = "Гонконг",
        ["Bangkok"] = "Бангкок",
        ["Sydney"] = "Сидней",
        ["Melbourne"] = "Мельбурн",
        ["New York"] = "Нью-Йорк",
        ["Los Angeles"] = "Лос-Анджелес",
        ["Chicago"] = "Чикаго",
        ["San Francisco"] = "Сан-Франциско",
        ["Seattle"] = "Сиэтл",
        ["Toronto"] = "Торонто",
        ["Vancouver"] = "Ванкувер",
        ["Montreal"] = "Монреаль",
        ["Bucharest"] = "Бухарест",
        ["Sofia"] = "София",
        ["Belgrade"] = "Белград",
        ["Zagreb"] = "Загреб",
        ["Ljubljana"] = "Любляна",
        ["Sarajevo"] = "Сараево",
        ["Skopje"] = "Скопье",
        ["Tirana"] = "Тирана",
        ["Podgorica"] = "Подгорица",
        ["Budapest"] = "Будапешт",
        ["Bratislava"] = "Братислава",
        ["Athens"] = "Афины",
        ["Nicosia"] = "Никосия",
        ["Valletta"] = "Валлетта"
    };

    public static string Country(string code, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(code) && Countries.TryGetValue(code, out var name))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(fallback) ? "Неизвестно" : fallback;
    }

    public static string City(string city)
        => !string.IsNullOrWhiteSpace(city) && Cities.TryGetValue(city, out var name) ? name : city;

    public static string Sub(string code, string city)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.IsNullOrWhiteSpace(city) ? "Страна не определена" : City(city);
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            return code;
        }

        return $"{code} · {City(city)}";
    }
}
