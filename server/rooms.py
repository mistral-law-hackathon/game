"""Game content: rooms, villains, clues and law cards (simplified French/EU law for education)."""

ROOMS = [
    {
        "id": "boss",
        "title": "Room 1 - The Open Space",
        "background": "bg_office",
        "villain": {
            "name": "Gerard, the Boss",
            "portrait": "boss",
            "persona": "Gerard, a loud, dramatic, ridiculous boss who thinks he owns his employees. "
                       "He shouts, guilt-trips, brags about his 'WORLD'S BEST BOSS' mug and invents fake laws to intimidate.",
            "opening": "YOU'RE FIRED! And don't even THINK about asking for those 'extra hours'. "
                       "Passion is its own reward. The door stays locked until you admit I'm right!",
        },
        "intro": "It's 9 p.m. You've worked 47 hours this week. Your boss Gerard just stuck a Post-it on your screen: "
                 "'You're fired. Leave the stapler.' The exit is locked. Search the office and ask Maitre Pocket for help.",
        "need": 2,
        "clues": [
            {"id": "payslip", "label": "Payslip", "card": "overtime",
             "text": "Your payslip: 35 hours paid. Your badge log: 47 hours worked. 12 hours vanished into Gerard's 'passion fund'."},
            {"id": "postit", "label": "Post-it note", "card": "procedure",
             "text": "A yellow Post-it: 'You're fired. Leave the stapler. - G'. No letter, no meeting, no reason given."},
            {"id": "poster", "label": "Weird poster", "card": "withdrawal",
             "text": "A poster: 'In case of fire, keep working.' Disturbing, but nothing in the office is actually dangerous right now."},
        ],
        "cards": [
            {"id": "overtime", "relevant": True, "title": "Overtime must be paid",
             "plain": "Hours worked beyond 35 per week are overtime. They must be paid extra: +25% for the first 8 extra hours, +50% after (unless a collective agreement sets other rates, at least +10%).",
             "example": "You work 40 hours: the 5 extra hours are paid 125%.",
             "law": "French Labour Code, Art. L3121-28 & L3121-36"},
            {"id": "procedure", "relevant": True, "title": "No firing by Post-it",
             "plain": "Before firing you, the employer must invite you in writing to a prior meeting, explain the reasons, listen to you, and then send a dismissal letter.",
             "example": "A text message 'you're fired' does not respect the procedure.",
             "law": "French Labour Code, Art. L1232-2 & L1232-6"},
            {"id": "cause", "relevant": True, "title": "A real and serious reason",
             "plain": "A dismissal needs a real and serious cause: true, objective facts. 'I don't like your face' or 'you asked for your pay' is not one. Otherwise a judge can award damages.",
             "example": "Firing someone because they claimed their overtime is unjustified.",
             "law": "French Labour Code, Art. L1232-1"},
            {"id": "withdrawal", "relevant": False, "title": "Right to withdraw (danger)",
             "plain": "You may stop working if there is a serious and imminent danger to your life or health, without losing pay.",
             "example": "Refusing to use a machine with a broken safety guard.",
             "law": "French Labour Code, Art. L4131-1"},
        ],
    },
    {
        "id": "hr",
        "title": "Room 2 - Human Resources",
        "background": "bg_hr",
        "villain": {
            "name": "Sandrine from HR",
            "portrait": "hr",
            "persona": "Sandrine, a sugary-sweet but creepy HR manager who spies on employees 'for their own good', "
                       "speaks in corporate buzzwords, smiles while threatening, and claims HR is above the law.",
            "opening": "Sweetie! Before you go, just sign this tiny non-compete. Oh, and I've read your private messages, "
                       "they were SO interesting. Sign, and the door opens!",
        },
        "intro": "You escaped Gerard. Now Sandrine from HR blocks the corridor with a contract and a printout of your private chats. "
                 "Look around and ask Maitre Pocket what she's allowed to do.",
        "need": 2,
        "clues": [
            {"id": "chats", "label": "Printed chats", "card": "privacy",
             "text": "A printout of your WhatsApp-style chat with your sister, in a folder you had named 'PERSONAL'. Sandrine highlighted the juicy parts."},
            {"id": "clause", "label": "Non-compete clause", "card": "noncompete",
             "text": "Clause 13: 'Employee shall not work in ANY industry, ANYWHERE in Europe, for 10 years. Compensation: a heartfelt thank-you.'"},
            {"id": "shredder", "label": "Shredder", "card": "secrets",
             "text": "In the shredder: your request 'Please send me a copy of the personal data you hold about me'. Sandrine shredded it."},
        ],
        "cards": [
            {"id": "privacy", "relevant": True, "title": "Private life at work",
             "plain": "Even at work you keep a right to privacy. Your employer cannot open messages or files you clearly marked as personal (except in narrow cases, with you present).",
             "example": "A folder named 'PERSONAL' on your work laptop is off-limits to your boss.",
             "law": "French Civil Code Art. 9; Cour de cassation, 'Nikon' ruling, 2 Oct. 2001"},
            {"id": "noncompete", "relevant": True, "title": "Fair non-compete only",
             "plain": "A non-compete clause is only valid if it protects a legitimate business interest, is limited in time and place, fits your job, AND comes with financial compensation.",
             "example": "'No work anywhere for 10 years, unpaid' is void.",
             "law": "Cour de cassation, social chamber, 10 July 2002"},
            {"id": "access", "relevant": True, "title": "Right of access to your data",
             "plain": "You can ask any organisation for a copy of the personal data it holds about you. It must answer, in principle within one month, for free.",
             "example": "Asking HR for everything in your personnel file.",
             "law": "GDPR Art. 15 & Art. 12(3)"},
            {"id": "secrets", "relevant": False, "title": "Trade secrets",
             "plain": "Protects a company's confidential business information (recipes, algorithms) against theft. It protects the company, not you.",
             "example": "An engineer leaking the Coca-Cola recipe.",
             "law": "EU Directive 2016/943"},
        ],
    },
    {
        "id": "landlord",
        "title": "Room 3 - The Exit",
        "background": "bg_exit",
        "villain": {
            "name": "Rocco, your landlord",
            "portrait": "landlord",
            "persona": "Rocco, a smooth-talking, greedy landlord who keeps every deposit, calls everything 'damage', "
                       "flashes cash, and claims 'landlord law' gives him all rights.",
            "opening": "Ah, my favourite ex-tenant! Your deposit? Gone, my friend. Repainting, 'emotional damages', "
                       "and a fee for the doorbell you rang. You want out? Thank me first.",
        },
        "intro": "Freedom is one revolving door away... but Rocco, the landlord of the flat you left 6 weeks ago, is waiting. "
                 "He still has your 1,000 EUR deposit. Check the papers and ask Maitre Pocket.",
        "need": 2,
        "clues": [
            {"id": "inventory", "label": "Exit inventory", "card": "deadline",
             "text": "Move-out inventory, signed by Rocco 6 weeks ago: 'Same condition as move-in.' Unfurnished flat, rent 800 EUR/month."},
            {"id": "invoice", "label": "Rocco's invoice", "card": "wear",
             "text": "Invoice: 'Repainting walls after 8 years of normal life: 900 EUR. Emotional damage: 100 EUR.'"},
            {"id": "flyer", "label": "Flyer", "card": "winter",
             "text": "A flyer about the winter period when tenants can't be evicted. Interesting, but you already moved out."},
        ],
        "cards": [
            {"id": "deadline", "relevant": True, "title": "Deposit back in 1 month",
             "plain": "If the exit inventory matches the entry one, the landlord must return the deposit within 1 month (2 months if there are differences). If late, he owes 10% of the monthly rent for each started month of delay.",
             "example": "6 weeks late on an 800 EUR rent: he owes the deposit plus 80 EUR penalty for the started month.",
             "law": "Law n. 89-462 of 6 July 1989, Art. 22"},
            {"id": "wear", "relevant": True, "title": "Normal wear is not damage",
             "plain": "A tenant pays for damage they caused, but not for normal ageing of the flat (paint fading after years, worn carpet). That's the landlord's cost.",
             "example": "Walls looking tired after 8 years cannot be billed to you.",
             "law": "Law n. 89-462 of 6 July 1989, Art. 7 (c)"},
            {"id": "proof", "relevant": True, "title": "The landlord must justify deductions",
             "plain": "A landlord can only keep part of the deposit for amounts actually owed, and must justify them (inventory, quotes, invoices). 'Emotional damage' is not a thing here.",
             "example": "Deducting for a broken window shown in the exit inventory, with a repair invoice.",
             "law": "Law n. 89-462 of 6 July 1989, Art. 22"},
            {"id": "winter", "relevant": False, "title": "Winter eviction truce",
             "plain": "From 1 November to 31 March, tenants generally cannot be evicted from their home.",
             "example": "A landlord can't throw a tenant out in January.",
             "law": "Code of Civil Enforcement Procedures, Art. L412-6"},
        ],
    },
]

BY_ID = {r["id"]: r for r in ROOMS}


def public_rooms():
    out = []
    for r in ROOMS:
        out.append({
            "id": r["id"], "title": r["title"], "background": r["background"], "intro": r["intro"], "need": r["need"],
            "villain": {k: r["villain"][k] for k in ("name", "portrait", "opening")},
            "clues": r["clues"], "cards": r["cards"],
        })
    return out
