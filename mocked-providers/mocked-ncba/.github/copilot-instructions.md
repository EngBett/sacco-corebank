Help me create mocked apis for NCBA bank as payments provider. The specification for the APIs is as follows:

The NCBA Payments API Testing Guide
The testing is done over the internet and uses keys to authenticate the transactions. The keys are
set per account. The key is tied to an account that will be debited when the key is used. The Key
will be at the header of the API.
The API testing is on developer site and url for test environment
https://devuat.ncbagroup.com/home

Test API Keys as below;
1. Kenya: ke123,
2. Tanzania: tz123
3. Uganda: ug123

The current payment methods covered are;
1. Internal Transactions – Transactions into NCBA Accounts.
2. EFT Transactions – Transactions into local bank account via ACH
3. RTGS Transactions – Transactions into local bank account via Swift
4. Mwallet Transactions – Transactions into an Mobile Wallet( Mpesa)
5. Pesalink Transactions – Transactions into local bank account via Kenya Local Switch

REQUIRED (MANDATORY) FIELDS
INTERNAL TRANSFERS
{
"BankCode": "string",
"BankSwiftCode": "string",
"BranchCode": "string",
"BeneficiaryAccountName": "string", - NAME OF THE BENEFICIARY
"BeneficiaryName": "string",- NAME OF THE BENEFICIARY
"Country": "string", - COUNTRY YOU ARE PAYING INTO (CASE SENSITIVE) – Kenya, Uganda,
Tanzania
"TranType": "string", - Internal
"Reference": "string", -Transaction Unique Identifier
"Currency": "string", - KES for Kenya, UGX for Uganda, TZS for Tanzania
"Account": "string", - Account you are paying into – Only numerical characters
"Amount": 0, - Amount - only numerical
"Narration": "string", alphanuemrical
"Transaction Date": "string" Optional
}

EFT
{
"BankCode": "string", - BANK CODE
"BankSwiftCode": "string", - Optional
"BranchCode": "string", BRANCH CODE
"BeneficiaryAccountName": "string", - NAME OF THE BENEFICIARY ACCOUNT
"BeneficiaryName": "string",- NAME OF THE RECIPIENT
"Country": "string", - COUNTRY YOU ARE PAYING INTO (CASE SENSITIVE) – Kenya, Uganda,
Tanzania
"TranType": "string", - Eft
"Reference": "string", -Transaction Unique Identifier
"Currency": "string", - KES for Kenya, UGX for Uganda, TZS for Tanzania
"Account": "string", - Account you are paying into for Mobile in the format of 2547XX XX XX XX
"Amount": 0, - Amount
"Narration": "string",
"Transaction Date": "string"
}

RTGS
{
"BankCode": "string", - BANK CODE (eg for NCBA its 07)
"BankSwiftCode": "string", SWIFT CODE (eg CITIKENA)
"BranchCode": "string", BRANCH CODE (eg for CITI NAIROBI its 000)
"BeneficiaryAccountName": "string", - NAME OF THE BENEFICIARY
"BeneficiaryName": "string",- NAME OF THE BENEFICIARY
"Country": "string", - COUNTRY YOU ARE PAYING INTO (CASE SENSITIVE) – Kenya, Uganda,
Tanzania
"TranType": "string", - RTGS
"Reference": "string", -Transaction Unique Identifier

"Currency": "string", - KES for Kenya, UGX for Uganda, TZS for Tanzania
"Account": "string", - Account NUMBER you are paying into
"Amount": 0, - Amount
"Narration": "string",
"Transaction Date": "string"
}

PESALINK
{
"BankCode": "string", - BANK CODE (for Pesalink its 404)
"BankSwiftCode": "string",
"BranchCode": "string", BRANCH CODE (eg for Barclays Bank would be 03000)
"BeneficiaryAccountName": "string", - NAME OF THE BENEFICIARY
"BeneficiaryName": "string",- NAME OF THE BENEFICIARY
"Country": "Kenya",
"TranType": "string", - Pesalink
"Reference": "string", -Transaction Unique Identifier
"Currency": "KES",
"Account": "string", - Account you are paying into
"Amount": 0, - Amount
"Narration": "string",
"Transaction Date": "string"
}

MWALLET(MPESA)

Send a validation request. This will validate the beneficiary mobile number and advise whether
it’s registered for Mpesa or not. Endpoint:
(https://devuat.ncbagroup.com/swagger/ui/index#!/MpesaPhoneNumberValidation/MpesaPh
oneNumberValidation_Validate )
{
"Mobile Number": "string",
"Reference": "string"
}
Make payment Request using the validation code provided in your validation request

{
"BankCode": "string", - BANK CODE (eg for all Mwallets is 99)
"BankSwiftCode": "string",
"BranchCode": "string", BRANCH CODE (eg for all Mwallets is 002)
"BeneficiaryAccountName": "string", - NAME OF THE BENEFICIARY
"Country": "string", - COUNTRY YOU ARE PAYING INTO (CASE SENSITIVE) – Kenya, Uganda,
Tanzania
"TranType": "string", - Mpesa/HalotelTz, AirtelTz, ZantelTz, TigoTz, VodacomTz
"Reference": "string", -Transaction Unique Identifier
"Currency": "string", - KES for Kenya, TZS for Tanzania
"Account": "string", - Account you are paying into Mobile in the format of 254XXX XX XX XX
"Amount": 0, - Amount
"Narration": "string",
"Transaction Date": "string"
"Validation ID": "string" - Validation code provided from the Validation Request
}

Payment Rules
Internal
1. Minimum Value of 10
2. Account numbers not to exceed 10 characters for Kenya, 14 characters for TZ and UG
3. Cross currency transactions not encouraged
4. Payments are not limited to the KEs. 999,999 rule

EFT
1. Minimum Value of KES 10
2. Account numbers cannot be alphanumeric
3. Only Local Currency allowed
4. Maximum of KES999,999 allowed in Kenya only
5. Odd cents are not allowed. .00 and .50 allowed.
6. Name should not have more than 35 characters

RTGS
1. Minimum Value of KES 50
2. Account numbers cannot be alphanumeric
3. Local Currency, USD, GBP and EUR allowed
4. Odd cents are not allowed. .00 and .50 allowed.
5. Name should not have more than 35 characters

Pesalink
1. Minimum Value of KES 50
2. Account numbers cannot be alphanumeric
3. Only Local Currency allowed
4. Maximum of KES999,999 allowed in Kenya only
5. Odd cents are not allowed. .00 and .50 allowed.
6. Name should not have more than 35 characters
7. It’s a Kenya Only transaction
8. Payment Reference has a limit of 12 characters

M-Wallet Top Ups
1. Minimum Value of KES 100 in Kenya and TZS 1000 in Tanzania
2. Account numbers to begin with country code; 254 for Kenya and 255 for TZ (254 XXX XXX XXX)
3. Account numbers cannot be alphanumeric
4. Only the country’s local currency allowed and supported
5. Maximum of KES150,000 in Kenya only and TZS 3,000,000
6. Odd cents are not allowed
7. Name should not have more than 35 characters
8. Payment Reference has a limit of 12 characters

ERROR CODES
0-"Internal System Error";
1-"Invalid Country";
2-"Invalid Transaction Type";
3-"Invalid Amount";
4-"Invalid Account";
5-"Invalid Reference";
6-"Invalid Mpesa Account";
7-"Mpesa has a maximum limit of 70000 per transaction";
8-"Mpesa has a minimum amount limit of 10 per transaction";
9- "Invalid EFT Amount";
10-"Invalid or Missing IBAN";
11-"DUPLICATE"
12-"INSUFFICIENT FUND"
13-"INVALID SWIFT CHAR"
14-"CURRENCY MISMATCH"

Other Rules
1. The Reference number must be unique as it will be validated against previous ones
2. The API User is advised to be unique to each inputter. It will be at the header of the API.
3. The VPN set up must be done by filling the attached form.
4. Transactions will only be accepted using the key provided and also tied to the VPN IP address
Provided


Sample API payments Payloads
API Key- 0x9A19938D
API User- Kenya123
EFT
{
"BankCode": "01",
"BankSwiftCode": "KCBLKENX",
"BranchCode": "096",
"BeneficiaryAccountName": "DOROTHY WANGU",
"Country": "Kenya",
"TranType": "Eft",
"Reference": "D45678",
"Currency": "KES",
"Account": "6989415",
"Amount": 100,
"Narration": "Salaries",
"Transaction Date": "20210324"
}

RTGS
{
"BankCode": "01",
"BankSwiftCode": "KCBLKENX",
"BranchCode": "096",
"BeneficiaryAccountName": "DOROTHY WANGU",
"Country": "Kenya",
"TranType": "RTGS",
"Reference": "D45678",
"Currency": "KES",
"Account": "698945",
"Amount": 100,
"Narration": "Salaries",
"Transaction Date": "20210324"
}
PESALINK

{
"BankCode":"404",
"BankSwiftCode":"KCBLKENX",
"BranchCode":"01000",

"BeneficiaryAccountName":"John Doe",
"BeneficiaryName":"John Doe",
"Country":"Kenya",
"TranType":"Pesalink",
"Reference":"202102220000297",
"Currency":"KES",
"Account":"5167788",
"Amount":15694.0,
"Narration":"Payment of funds",
"Transaction Date":"20210315",
}

MPESA
{
"BankCode": "99",
"BankSwiftCode": "CBAFKENX",
"BranchCode": "002",
"BeneficiaryAccountName": "WANGU",
"Country": "Kenya",
"TranType": "Mpesa",
"Reference": "234567",
"Currency": "KES",
"Account": "2547235812318",
"Amount": 100,
"Narration": "testing Mpesa",
"Transaction Date": "20210715",
"Validation ID": "PGF4D1F2HQ",
}

INTERNAL
{
"BankCode": "07",
"BankSwiftCode": "CBAFKENX",
"BranchCode": "000",
"BeneficiaryAccountName": "DOROTHY WANGU",
"Country": "Kenya",
"TranType": "Internal",
"Reference": "D45678",
"Currency": "KES",
"Account": " 2326540028",
"Amount": 100,

"Narration": "TESTING Internal",
"Transaction Date": "20210324"
}